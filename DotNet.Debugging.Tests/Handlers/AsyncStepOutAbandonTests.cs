using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A step out of an async method waits for its task through a breakpoint in the runtime. A user breakpoint hit
// meanwhile abandons the step, so the task's completion must not produce a step stop the user never asked for
public class AsyncStepOutAbandonTests : BaseDebugTestFixture {
    public AsyncStepOutAbandonTests() : base(nameof(AsyncStepOutAbandonTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        await WorkAsync();
        Console.WriteLine("after"); // marker:afterWork

        static async Task WorkAsync() {
            var started = 1; // marker:workStart
            await Task.Delay(50);
            Console.WriteLine(started); // marker:insideWork
            await Task.Delay(50);
        }
        """;
    }

    [Test]
    public void BreakpointStopAbandonsAsyncStepOutTest() {
        var threadId = LaunchToMarker("marker:workStart");
        SetBreakpoints(GetMarkerLine("marker:workStart"), GetMarkerLine("marker:insideWork"));

        Host.SendRequestSync(new StepOutRequest() { ThreadId = threadId });
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:insideWork")), "The breakpoint inside the awaited work wins over the step out");

        Continue(stopped.ThreadId!.Value);
        var stops = CollectStopsUntilExit();
        Assert.That(stops, Is.Empty, "The abandoned step out must not fire once the awaited task completes");
    }
}
