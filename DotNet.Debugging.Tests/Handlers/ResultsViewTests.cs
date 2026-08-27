using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// The debuggee never touches LINQ, so 'System.Linq.dll' must be loaded into it before the enumeration compiles
public class ResultsViewTests : BaseDebugTestFixture {
    public ResultsViewTests() : base(nameof(ResultsViewTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var collection = new MyCollection();
        collection.Add("first");
        collection.Add("second");
        var empty = new MyCollection();
        Console.WriteLine($"{collection}{empty}"); // marker:stop
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
    public void ResultsViewWithoutSystemLinqLoadedTest() {
        var threadId = StopAtMarker();

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
        var threadId = StopAtMarker();

        var empty = GetLocalVariables(threadId).First(it => it.Name == "empty [MyCollection]");
        var members = GetVariables(empty.VariablesReference);
        var resultsView = members.First(it => it.Name == "Results View");

        var items = GetVariables(resultsView.VariablesReference);
        Assert.That(items, Has.Count.EqualTo(1), "An empty enumeration shows a single message row instead of nothing");
        Assert.That(items[0].Name, Is.EqualTo("Empty [string]"));
        Assert.That(items[0].Value, Is.EqualTo("\"Enumeration yielded no results\""));
        Assert.That(items[0].VariablesReference, Is.Zero, "The message row has no children");
    }
}
