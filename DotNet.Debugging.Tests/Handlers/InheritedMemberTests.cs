using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class InheritedMemberTests : BaseDebugTestFixture {
    public InheritedMemberTests() : base(nameof(InheritedMemberTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        using Docs;

        var sheet = new Worksheet();
        var budget = new BudgetSheet();
        var page = new TitlePage();
        var kind = sheet.Type;
        Console.WriteLine($"{sheet.Type} {sheet.Name} {sheet.Index} {budget.Name} {page.Content} {kind}"); // marker:stop
        Console.WriteLine("done");

        namespace Docs {
            public enum SheetType { Worksheet, Chart }

            public interface IPart {
                int Index { get; }
                string Label { get; }
            }

            public abstract class Sheet : IPart {
                protected int id = 1;
                public abstract SheetType Type { get; }
                public virtual string Name => "sheet";
                public int Index => 7;
                string IPart.Label => "part";
            }

            public class Worksheet : Sheet {
                public override SheetType Type => SheetType.Worksheet;
                public override string Name => "worksheet";
            }

            public class BudgetSheet : Worksheet {
                public override string Name => "budget";
            }

            public class Page {
                protected int revision = 1;
                protected internal int weight = 5;
                internal string Author => "editor";
                public virtual object Content => 42;
            }

            public class TitlePage : Page {
                protected new int revision = 2;
                public new string Content => "title";
            }
        }
        """;
    }

    [Test]
    public void OverriddenPropertyListedOnceTest() {
        var threadId = LaunchToMarker();
        var members = GetMembers(threadId, "sheet");

        // 'Type' and 'Name' are declared by 'Sheet' and overridden by 'Worksheet' - one property, one entry
        Assert.That(members.Where(it => it.Name.StartsWith("Type ")).ToList(), Has.Count.EqualTo(1));
        Assert.That(members.Where(it => it.Name.StartsWith("Name ")).ToList(), Has.Count.EqualTo(1));
        Assert.That(members.First(it => it.Name.StartsWith("Type ")).Value, Is.EqualTo("Worksheet"));
        Assert.That(members.First(it => it.Name.StartsWith("Name ")).Value, Is.EqualTo("\"worksheet\""));
    }

    [Test]
    public void InheritedMemberStillListedTest() {
        var threadId = LaunchToMarker();
        var members = GetMembers(threadId, "sheet");

        // A base member no derived type overrides is listed under the type that declares it
        var index = members.Where(it => it.Name.StartsWith("Index ")).ToList();
        Assert.That(index, Has.Count.EqualTo(1));
        Assert.That(index[0].Value, Is.EqualTo("7"));

        var nonPublic = members.First(it => it.Name == "Non-Public members");
        var inheritedField = GetVariables(nonPublic.VariablesReference).FirstOrDefault(it => it.Name.StartsWith("id "));
        Assert.That(inheritedField, Is.Not.Null);
        Assert.That(inheritedField!.Value, Is.EqualTo("1"));
    }

    // Overridden again two levels down, still one entry, holding the most derived value
    [Test]
    public void OverrideChainListedOnceTest() {
        var threadId = LaunchToMarker();
        var members = GetMembers(threadId, "budget");

        Assert.That(members.Where(it => it.Name.StartsWith("Name ")).ToList(), Has.Count.EqualTo(1));
        Assert.That(members.First(it => it.Name.StartsWith("Name ")).Value, Is.EqualTo("\"budget\""));
        Assert.That(members.Where(it => it.Name.StartsWith("Type ")).ToList(), Has.Count.EqualTo(1));
    }

    // An implicit interface implementation is the class's own property, an explicit one is listed with the
    // public members under its interface-qualified name, the way Microsoft's debugger shows it for a type with symbols
    [Test]
    public void InterfaceImplementationTest() {
        var threadId = LaunchToMarker();
        var members = GetMembers(threadId, "sheet");

        Assert.That(members.Where(it => it.Name.StartsWith("Index ")).ToList(), Has.Count.EqualTo(1));
        var label = members.FirstOrDefault(it => it.Name.StartsWith("Docs.IPart.Label "));
        Assert.That(label, Is.Not.Null);
        Assert.That(label!.Value, Is.EqualTo("\"part\""));
        Assert.That(label.EvaluateName, Is.EqualTo("((Docs.IPart)sheet).Label"));
        Assert.That(Evaluate(label.EvaluateName!, threadId).Result, Is.EqualTo("\"part\""));
    }

    // A 'new' property hides the base one rather than replacing it: both are listed, the hidden one under its
    // declaring type, and it is reached through a cast to that type
    [Test]
    public void HiddenPropertyListedWithDeclaringTypeTest() {
        var threadId = LaunchToMarker();
        var members = GetMembers(threadId, "page");

        var content = members.Where(it => it.Name.StartsWith("Content ")).ToList();
        Assert.That(content, Has.Count.EqualTo(2));
        var own = content.First(it => it.Name.StartsWith("Content ["));
        var hidden = content.First(it => it.Name.StartsWith("Content (Docs.Page) ["));
        Assert.That(own.Value, Is.EqualTo("\"title\""));
        Assert.That(hidden.Value, Is.EqualTo("42"));
        Assert.That(own.EvaluateName, Is.EqualTo("page.Content"));
        Assert.That(hidden.EvaluateName, Is.EqualTo("((Docs.Page)page).Content"));
        Assert.That(Evaluate(hidden.EvaluateName!, threadId).Result, Is.EqualTo("42"));
    }

    [Test]
    public void HiddenFieldListedWithDeclaringTypeTest() {
        var threadId = LaunchToMarker();
        var members = GetMembers(threadId, "page");

        var nonPublic = GetVariables(members.First(it => it.Name == "Non-Public members").VariablesReference);
        var revisions = nonPublic.Where(it => it.Name.StartsWith("revision ")).ToList();
        Assert.That(revisions, Has.Count.EqualTo(2));
        Assert.That(revisions.First(it => it.Name.StartsWith("revision [")).Value, Is.EqualTo("2"));
        Assert.That(revisions.First(it => it.Name.StartsWith("revision (Docs.Page) [")).Value, Is.EqualTo("1"));
    }

    // Internal and protected internal members read better inline than behind the group, protected ones stay there
    [Test]
    public void InternalMembersListedInlineTest() {
        var threadId = LaunchToMarker();
        var members = GetMembers(threadId, "page");

        Assert.That(members.FirstOrDefault(it => it.Name.StartsWith("Author "))?.Value, Is.EqualTo("\"editor\""));
        Assert.That(members.FirstOrDefault(it => it.Name.StartsWith("weight "))?.Value, Is.EqualTo("5"));
        Assert.That(members.Any(it => it.Name.StartsWith("revision ")), Is.False);
        var nonPublic = GetVariables(members.First(it => it.Name == "Non-Public members").VariablesReference);
        Assert.That(nonPublic.Any(it => it.Name.StartsWith("revision ")), Is.True);
        Assert.That(nonPublic.Any(it => it.Name.StartsWith("Author ") || it.Name.StartsWith("weight ")), Is.False);
    }

    [Test]
    public void EnumLocalHasNoChildrenTest() {
        var threadId = LaunchToMarker();
        var locals = GetLocalVariables(threadId);

        // The member name is the whole value of an enum, 'value__' and the constants of the type are not shown
        var kind = locals.First(it => it.Name.StartsWith("kind "));
        Assert.That(kind.Value, Is.EqualTo("Worksheet"));
        Assert.That(kind.VariablesReference, Is.EqualTo(0));
    }

    [Test]
    public void EnumPropertyHasNoChildrenTest() {
        var threadId = LaunchToMarker();
        var members = GetMembers(threadId, "sheet");

        var type = members.First(it => it.Name.StartsWith("Type "));
        Assert.That(type.VariablesReference, Is.EqualTo(0));
    }

    private List<Variable> GetMembers(int threadId, string localName) {
        var local = GetLocalVariables(threadId).First(it => it.Name.StartsWith(localName + " "));
        return GetVariables(local.VariablesReference);
    }
}
