using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// The debuggee never touches LINQ, so 'System.Linq.dll' must be loaded into it before the enumeration compiles
public class ResultsViewTests : BaseDebugTestFixture {
    public ResultsViewTests() : base(nameof(ResultsViewTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        using System.Collections.Immutable;

        var collection = new MyCollection();
        collection.Add("first");
        collection.Add("second");
        var empty = new MyCollection();
        var structCollection = new MyStructCollection(new[] { "first", "second" });
        var immutable = ImmutableArray.Create("first", "second");
        Console.WriteLine($"{collection}{empty}{structCollection}{immutable.Length}"); // marker:stop
        Console.WriteLine("done");

        public class MyCollection : IEnumerable<string> {
            private readonly List<string> innerCollection = new List<string>();

            public void Add(string item) {
                innerCollection.Add(item);
            }
            public IEnumerator<string> GetEnumerator() {
                return innerCollection.GetEnumerator();
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
                return innerCollection.GetEnumerator();
            }
        }

        public struct MyStructCollection : IEnumerable<string> {
            private readonly string[] items;

            public MyStructCollection(string[] items) {
                this.items = items;
            }
            public IEnumerator<string> GetEnumerator() {
                return ((IEnumerable<string>)items).GetEnumerator();
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
                return items.GetEnumerator();
            }
        }
        """;
    }

    [Test]
    public void ResultsViewWithoutSystemLinqLoadedTest() {
        var threadId = LaunchToMarker();

        var collection = GetLocalVariables(threadId).First(it => it.Name == "collection [MyCollection]");
        var members = GetVariables(collection.VariablesReference);
        var resultsView = members.First(it => it.Name == "Results View");

        var items = GetVariables(resultsView.VariablesReference);
        Assert.That(items.Select(it => it.Name), Is.EqualTo(new[] { "[0] [string]", "[1] [string]" }), "The enumeration loads System.Linq into the debuggee on demand");
        Assert.That(items[0].Value, Is.EqualTo("\"first\""));
        Assert.That(items[1].Value, Is.EqualTo("\"second\""));
    }

    [Test]
    public void EmptyResultsViewTest() {
        var threadId = LaunchToMarker();

        var empty = GetLocalVariables(threadId).First(it => it.Name == "empty [MyCollection]");
        var members = GetVariables(empty.VariablesReference);
        var resultsView = members.First(it => it.Name == "Results View");

        var items = GetVariables(resultsView.VariablesReference);
        Assert.That(items, Has.Count.EqualTo(1), "An empty enumeration shows a single message row instead of nothing");
        Assert.That(items[0].Name, Is.EqualTo("Empty"));
        Assert.That(items[0].Value, Is.EqualTo("Enumeration yielded no results"));
        Assert.That(items[0].VariablesReference, Is.Zero, "The message row has no children");
    }

    // The enumeration is compiled as a method of the value's type, and a struct method takes its 'this' by reference
    [Test]
    public void StructResultsViewTest() {
        var threadId = LaunchToMarker();

        var collection = GetLocalVariables(threadId).First(it => it.Name == "structCollection [MyStructCollection]");
        var members = GetVariables(collection.VariablesReference);
        var resultsView = members.First(it => it.Name == "Results View");

        var items = GetVariables(resultsView.VariablesReference);
        Assert.That(items.Select(it => it.Name), Is.EqualTo(new[] { "[0] [string]", "[1] [string]" }), "A struct enumerates like a class");
        Assert.That(items.Select(it => it.Value), Is.EqualTo(new[] { "\"first\"", "\"second\"" }));
    }

    [Test]
    public void WatchedGenericStructResultsViewTest() {
        var threadId = LaunchToMarker();

        var immutable = Evaluate("immutable", threadId);
        Assert.That(immutable.Type, Is.EqualTo("System.Collections.Immutable.ImmutableArray<string>"));
        var members = GetVariables(immutable.VariablesReference);
        var resultsView = members.First(it => it.Name == "Results View");

        var items = GetVariables(resultsView.VariablesReference);
        Assert.That(items.Select(it => it.Name), Is.EqualTo(new[] { "[0] [string]", "[1] [string]" }), "A watched generic struct enumerates from the evaluation result");
        Assert.That(items.Select(it => it.Value), Is.EqualTo(new[] { "\"first\"", "\"second\"" }));
    }
}
