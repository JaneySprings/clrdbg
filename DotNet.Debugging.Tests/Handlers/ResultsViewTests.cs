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
        Console.WriteLine($"{collection}"); // marker:stop
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

    [Test]
    public void ResultsViewWithoutSystemLinqLoadedTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:stop"));
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var threadId = stopped.ThreadId!.Value;

        var collection = GetLocalVariables(threadId).First(it => it.Name == "collection [MyCollection]");
        var members = GetVariables(collection.VariablesReference);
        var resultsView = members.First(it => it.Name == "Results View");

        var items = GetVariables(resultsView.VariablesReference);
        Assert.That(items.Select(it => it.Name), Is.EqualTo(new[] { "[0] [string]", "[1] [string]" }), "The enumeration loads System.Linq into the debuggee on demand");
        Assert.That(items[0].Value, Is.EqualTo("\"first\""));
        Assert.That(items[1].Value, Is.EqualTo("\"second\""));
    }
}
