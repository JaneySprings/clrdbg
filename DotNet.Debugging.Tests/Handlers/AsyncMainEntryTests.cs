using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// An async Main is entered through the compiler's '<Main>' bridge, which has no source: the entry stop goes to Main itself
public class AsyncMainEntryTests : BaseDebugTestFixture {
    public AsyncMainEntryTests() : base(nameof(AsyncMainEntryTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        public static class Program {
            public static async Task Main(string[] args) { // marker:main
                await Task.Delay(1);
                Console.WriteLine("hello"); // marker:stop
            }
        }
        """;
    }

    [Test]
    public void StopAtEntryOfAsyncMainTest() {
        Launch(stopAtEntry: true);
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Entry);
        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Source, Is.Not.Null, "The entry stop has a source");
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:main")), "The stop is at the start of Main, not in the bridge");
    }
}
