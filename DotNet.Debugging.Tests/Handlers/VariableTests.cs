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
        var custom = new CustomCollection();
        custom.Add("first");
        custom.Add("second");
        var legacy = new LegacyCollection();
        var matches = System.Text.RegularExpressions.Regex.Matches("aa baa", "a+");
        Console.WriteLine($"{count} {title} {numbers.Length} {item.PublicProperty} {builder.Length} {custom} {legacy} {matches.Count}"); // marker:stop
        Console.WriteLine("done");

        public class SampleClass {
            public int PublicField = 1;
            private int privateField = 2;
            public int PublicProperty => 10;
            private int PrivateProperty => 20;
            public int this[int index] => index;
            public static int StaticPublicField = 100;
        }

        public class CustomCollection : IEnumerable<string> {
            private readonly List<string> innerItems = new List<string>();

            public void Add(string item) {
                innerItems.Add(item);
            }
            public IEnumerator<string> GetEnumerator() {
                return innerItems.GetEnumerator();
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
                return innerItems.GetEnumerator();
            }
        }

        public class LegacyCollection : System.Collections.IEnumerable {
            public System.Collections.IEnumerator GetEnumerator() {
                return new object[] { 1, 2 }.GetEnumerator();
            }
        }
        """;
    }

    [Test]
    public void DisplayNameTest() {
        var threadId = LaunchToMarker();
        var locals = GetLocalVariables(threadId);

        var count = locals.FirstOrDefault(it => it.Name == "count [int]");
        Assert.That(count, Is.Not.Null, "Variables are displayed with the type in brackets");
        Assert.That(count!.Value, Is.EqualTo("42"));
        Assert.That(count.EvaluateName, Is.EqualTo("count"), "The evaluate name must not contain the type suffix");

        var title = locals.FirstOrDefault(it => it.Name == "title [string]");
        Assert.That(title, Is.Not.Null);
        Assert.That(title!.Value, Is.EqualTo("\"hello\""));

        var numbers = locals.FirstOrDefault(it => it.Name == "numbers [int[]]");
        Assert.That(numbers, Is.Not.Null, "Arrays have the type suffix too");
        Assert.That(numbers!.Value, Is.EqualTo("{int[60]}"));
        var item = locals.FirstOrDefault(it => it.Name == "item [SampleClass]");
        Assert.That(item, Is.Not.Null, "Objects have the type suffix too");
        Assert.That(item!.Value, Is.EqualTo("{SampleClass}"));
        Assert.That(item.EvaluateName, Is.EqualTo("item"));
        var builder = locals.FirstOrDefault(it => it.Name == "builder [StringBuilder]");
        Assert.That(builder, Is.Not.Null, "The suffix shows the type without its namespace");
        Assert.That(builder!.Type, Is.EqualTo("System.Text.StringBuilder"), "The type itself stays fully qualified");
    }

    [Test]
    public void EvaluateNamesArePathsTest() {
        var threadId = LaunchToMarker();
        var locals = GetLocalVariables(threadId);

        var item = locals.First(it => it.Name == "item [SampleClass]");
        var members = GetVariables(item.VariablesReference);
        var publicField = members.First(it => it.Name == "PublicField [int]");
        Assert.That(publicField.EvaluateName, Is.EqualTo("item.PublicField"));
        Assert.That(publicField.PresentationHint?.Kind, Is.EqualTo(VariablePresentationHint.KindValue.Data));
        Assert.That(publicField.PresentationHint?.Visibility, Is.EqualTo(VariablePresentationHint.VisibilityValue.Public));
        var nonPublicGroup = members.First(it => it.Name == "Non-Public members");
        var privateProperty = GetVariables(nonPublicGroup.VariablesReference).First(it => it.Name == "PrivateProperty [int]");
        Assert.That(privateProperty.EvaluateName, Is.EqualTo("item.PrivateProperty"), "Members of a group build their expression from the grouped value");
        Assert.That(privateProperty.PresentationHint?.Kind, Is.EqualTo(VariablePresentationHint.KindValue.Property));
        Assert.That(privateProperty.PresentationHint?.Visibility, Is.EqualTo(VariablePresentationHint.VisibilityValue.Private));

        var staticGroup = members.First(it => it.Name == "Static members");
        Assert.That(staticGroup.EvaluateName, Is.Null, "Pseudo nodes are not expressions");
        var staticField = GetVariables(staticGroup.VariablesReference).First(it => it.Name == "StaticPublicField [int]");
        Assert.That(staticField.EvaluateName, Is.EqualTo("SampleClass.StaticPublicField"));

        var numbers = locals.First(it => it.Name == "numbers [int[]]");
        var firstPage = GetVariables(numbers.VariablesReference);
        Assert.That(firstPage[0].EvaluateName, Is.EqualTo("numbers[0]"));
        var more = firstPage[^1];
        Assert.That(more.Name, Is.EqualTo("[More]"));
        Assert.That(more.PresentationHint?.Attributes, Is.EqualTo(VariablePresentationHint.AttributesValue.ReadOnly));
        var secondPage = GetVariables(more.VariablesReference);
        Assert.That(secondPage[0].EvaluateName, Is.EqualTo("numbers[25]"));
    }

    [Test]
    public void VariablesPagingTest() {
        var threadId = LaunchToMarker();
        var numbers = GetLocalVariables(threadId).First(it => it.Name == "numbers [int[]]");

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
    public void UserTypeNonPublicMembersGroupTest() {
        var threadId = LaunchToMarker();
        var item = GetLocalVariables(threadId).First(it => it.Name == "item [SampleClass]");

        var members = GetVariables(item.VariablesReference);
        Assert.That(members.Select(it => it.Name), Is.EqualTo(new[] { "PublicField [int]", "PublicProperty [int]", "Static members", "Non-Public members" }),
            "User code types group their non-public members like library types");

        var nonPublicGroup = members.First(it => it.Name == "Non-Public members");
        var nonPublicNames = GetVariables(nonPublicGroup.VariablesReference).Select(it => it.Name);
        Assert.That(nonPublicNames, Is.EqualTo(new[] { "PrivateProperty [int]", "privateField [int]" }), "The group holds the non-public members in ordinal order");
    }

    [Test]
    public void SystemTypeNonPublicMembersGroupTest() {
        var threadId = LaunchToMarker();
        var builder = GetLocalVariables(threadId).First(it => it.Name == "builder [StringBuilder]");

        var members = GetVariables(builder.VariablesReference);
        Assert.That(members.Any(it => it.Name == "Non-Public members"), "Library types must group their non-public members");
        Assert.That(members.Any(it => it.Name.StartsWith("m_")), Is.False, "Non-public members must not be shown inline");
        Assert.That(members.Any(it => it.Name.StartsWith("Chars")), Is.False, "Indexers cannot be evaluated without arguments and must be hidden");

        var nonPublicGroup = members.First(it => it.Name == "Non-Public members");
        var nonPublicMembers = GetVariables(nonPublicGroup.VariablesReference);
        Assert.That(nonPublicMembers.Any(it => it.Name.StartsWith("m_")), "The non-public members of StringBuilder are expected in the group");
    }

    [Test]
    public void ResultsViewTest() {
        var threadId = LaunchToMarker();
        var custom = GetLocalVariables(threadId).First(it => it.Name == "custom [CustomCollection]");

        var members = GetVariables(custom.VariablesReference);
        var resultsView = members[^1];
        Assert.That(resultsView.Name, Is.EqualTo("Results View"), "Enumerable values offer a deferred enumeration node, sorted last");
        Assert.That(resultsView.Value, Is.EqualTo("Expanding the Results View will enumerate the IEnumerable"));

        var items = GetVariables(resultsView.VariablesReference);
        Assert.That(items.Select(it => it.Name), Is.EqualTo(new[] { "[0] [string]", "[1] [string]" }), "Expanding the node enumerates the value");
        Assert.That(items[0].Value, Is.EqualTo("\"first\""));
        Assert.That(items[1].Value, Is.EqualTo("\"second\""));

        var nonPublicGroup = members.First(it => it.Name == "Non-Public members");
        var innerList = GetVariables(nonPublicGroup.VariablesReference).First(it => it.Name.StartsWith("innerItems"));
        var listMembers = GetVariables(innerList.VariablesReference);
        Assert.That(listMembers.Any(it => it.Name == "Results View"), Is.False, "A value shown through a DebuggerTypeProxy already enumerates through the proxy");
        var rawView = listMembers.First(it => it.Name == "Raw View");
        Assert.That(GetVariables(rawView.VariablesReference).Any(it => it.Name == "Results View"), Is.False, "The 'Raw View' shows the plain members only");
    }

    [Test]
    public void ClosedGenericProxyTest() {
        var threadId = LaunchToMarker();
        var matches = GetLocalVariables(threadId).First(it => it.Name == "matches [MatchCollection]");
        Assert.That(matches.Value, Is.EqualTo("Count = 2"), "The DebuggerDisplay of MatchCollection must run, not a proxy creation error");

        // MatchCollection's proxy is 'CollectionDebuggerProxy`1[...Match]' - closed over Match in the serialized
        // name, its type argument comes from that name rather than from the value's own type parameters
        var members = GetVariables(matches.VariablesReference);
        Assert.That(members.Select(it => it.Name), Is.EqualTo(new[] { "[0] [Match]", "[1] [Match]", "Raw View" }), "The proxy's 'Items' array stands in for the members");
    }

    [Test]
    public void NonGenericResultsViewTest() {
        var threadId = LaunchToMarker();
        var legacy = GetLocalVariables(threadId).First(it => it.Name == "legacy [LegacyCollection]");

        var members = GetVariables(legacy.VariablesReference);
        var resultsView = members.First(it => it.Name == "Results View");
        var items = GetVariables(resultsView.VariablesReference);
        Assert.That(items.Select(it => it.Name), Is.EqualTo(new[] { "[0] [int]", "[1] [int]" }), "The items of a non generic IEnumerable are enumerated as objects");
        Assert.That(items[0].Value, Is.EqualTo("1"));
    }

    [Test]
    public void SetVariableTest() {
        var threadId = LaunchToMarker();
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

    [Test]
    public void SetArrayElementTest() {
        var threadId = LaunchToMarker();
        var numbers = GetLocalVariables(threadId).First(it => it.Name == "numbers [int[]]");

        var response = Host.SendRequestSync(new SetVariableRequest() {
            VariablesReference = numbers.VariablesReference,
            Name = "[0] [int]",
            Value = "99",
        });
        Assert.That(response.Value, Is.EqualTo("99"));
        Assert.That(Evaluate("numbers[0]", threadId).Result, Is.EqualTo("99"));
    }

    [Test]
    public void SetMemberFieldTest() {
        var threadId = LaunchToMarker();
        var item = GetLocalVariables(threadId).First(it => it.Name == "item [SampleClass]");

        var response = Host.SendRequestSync(new SetVariableRequest() {
            VariablesReference = item.VariablesReference,
            Name = "PublicField [int]",
            Value = "77",
        });
        Assert.That(response.Value, Is.EqualTo("77"));
        Assert.That(Evaluate("item.PublicField", threadId).Result, Is.EqualTo("77"));
    }

    [Test]
    public void SetReferenceToNullTest() {
        var threadId = LaunchToMarker();
        var frame = GetTopStackFrame(threadId);
        var scopes = Host.SendRequestSync(new ScopesRequest() { FrameId = frame.Id });

        var response = Host.SendRequestSync(new SetVariableRequest() {
            VariablesReference = scopes.Scopes[0].VariablesReference,
            Name = "title [string]",
            Value = "null",
        });
        Assert.That(response.Value, Is.EqualTo("null"));
        Assert.That(Evaluate("title", threadId).Result, Is.EqualTo("null"));
    }

    [Test]
    public void SetVariableWithInvalidValueFailsTest() {
        var threadId = LaunchToMarker();
        var frame = GetTopStackFrame(threadId);
        var scopes = Host.SendRequestSync(new ScopesRequest() { FrameId = frame.Id });

        Assert.Throws<Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.ProtocolException>(() => Host.SendRequestSync(new SetVariableRequest() {
            VariablesReference = scopes.Scopes[0].VariablesReference,
            Name = "count [int]",
            Value = "abc",
        }));
        Assert.That(Evaluate("count", threadId).Result, Is.EqualTo("42"), "A failed assignment leaves the value untouched");
    }
}