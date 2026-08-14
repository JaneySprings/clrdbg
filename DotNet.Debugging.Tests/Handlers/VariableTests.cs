using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class VariableTests : BaseDebugTestFixture {
    public VariableTests() : base(nameof(VariableTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var count = 42;
        var title = "hello";
        var numbers = Enumerable.Range(0, 60).ToArray();
        var item = new SampleClass();
        var builder = new System.Text.StringBuilder("abc");
        Console.WriteLine($"{count} {title} {numbers.Length} {item.PublicProperty} {builder.Length}"); // marker:stop
        Console.WriteLine("done");

        public class SampleClass {
            public int PublicField = 1;
            private int privateField = 2;
            public int PublicProperty => 10;
            private int PrivateProperty => 20;
            public static int StaticPublicField = 100;
        }
        """;
    }

    private int StopAtMarker() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:stop"));
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        return stopped.ThreadId!.Value;
    }

    [Test]
    public void SimpleTypeDisplayNameTest() {
        var threadId = StopAtMarker();
        var locals = GetLocalVariables(threadId);

        var count = locals.FirstOrDefault(it => it.Name == "count [int]");
        Assert.That(count, Is.Not.Null, "Simple types must be displayed with the type in brackets");
        Assert.That(count!.Value, Is.EqualTo("42"));
        Assert.That(count.EvaluateName, Is.EqualTo("count"), "The evaluate name must not contain the type suffix");

        var title = locals.FirstOrDefault(it => it.Name == "title [string]");
        Assert.That(title, Is.Not.Null);
        Assert.That(title!.Value, Is.EqualTo("\"hello\""));

        Assert.That(locals.Any(it => it.Name == "numbers"), "Arrays are not simple types and must not have the type suffix");
    }

    [Test]
    public void VariablesPagingTest() {
        var threadId = StopAtMarker();
        var numbers = GetLocalVariables(threadId).First(it => it.Name == "numbers");

        var firstPage = GetVariables(numbers.VariablesReference);
        Assert.That(firstPage, Has.Count.EqualTo(26), "25 items and the '[More]' node");
        Assert.That(firstPage[0].Name, Is.EqualTo("[0] [int]"));
        Assert.That(firstPage[^1].Name, Is.EqualTo("[More]"));
        Assert.That(firstPage[^1].Value, Is.Empty);

        var secondPage = GetVariables(firstPage[^1].VariablesReference);
        Assert.That(secondPage[0].Name, Is.EqualTo("[25] [int]"));
        Assert.That(secondPage[0].Value, Is.EqualTo("25"));
        Assert.That(secondPage[^1].Name, Is.EqualTo("[More]"));

        var thirdPage = GetVariables(secondPage[^1].VariablesReference);
        Assert.That(thirdPage, Has.Count.EqualTo(10), "The last page has no '[More]' node");
        Assert.That(thirdPage[^1].Name, Is.EqualTo("[59] [int]"));
    }

    [Test]
    public void UserTypeMembersAreFlatTest() {
        var threadId = StopAtMarker();
        var item = GetLocalVariables(threadId).First(it => it.Name == "item");

        var members = GetVariables(item.VariablesReference);
        var names = members.Select(it => it.Name).ToList();
        Assert.That(names, Is.EqualTo(new[] {
            "PrivateProperty [int]", "PublicField [int]", "PublicProperty [int]", "privateField [int]", "Static members"
        }), "User code types show all members inline in ordinal order");
    }

    [Test]
    public void SystemTypeNonPublicMembersGroupTest() {
        var threadId = StopAtMarker();
        var builder = GetLocalVariables(threadId).First(it => it.Name == "builder");

        var members = GetVariables(builder.VariablesReference);
        Assert.That(members.Any(it => it.Name == "Non-Public members"), "Library types must group their non-public members");
        Assert.That(members.Any(it => it.Name.StartsWith("m_")), Is.False, "Non-public members must not be shown inline");

        var nonPublicGroup = members.First(it => it.Name == "Non-Public members");
        var nonPublicMembers = GetVariables(nonPublicGroup.VariablesReference);
        Assert.That(nonPublicMembers.Any(it => it.Name.StartsWith("m_")), "The non-public members of StringBuilder are expected in the group");
    }

    [Test]
    public void SetVariableTest() {
        var threadId = StopAtMarker();
        var frame = GetTopStackFrame(threadId);
        var scopes = Host.SendRequestSync(new ScopesRequest() { FrameId = frame.Id });

        // Clients send the display name back when the user edits a variable
        var response = Host.SendRequestSync(new SetVariableRequest() {
            VariablesReference = scopes.Scopes[0].VariablesReference,
            Name = "count [int]",
            Value = "100",
        });
        Assert.That(response.Value, Is.EqualTo("100"));

        var result = Evaluate("count", threadId);
        Assert.That(result.Result, Is.EqualTo("100"));
    }
}
