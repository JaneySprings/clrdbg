using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A recursive async method runs its inner activations synchronously up to their first await, on the same thread
// and deeper in the stack: their yields hit the stepping activation's yield breakpoints and must not carry its step
public class AsyncRecursionSteppingTests : BaseDebugTestFixture {
    public AsyncRecursionSteppingTests() : base(nameof(AsyncRecursionSteppingTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var result = await Walk(3);
        Console.WriteLine(result); // marker:end

        static async Task<int> Walk(int n) {
            if (n == 0)
                return 0;
            var inner = await Walk(n - 1); // marker:recurse
            await Task.Delay(10); // marker:delay
            return inner + n; // marker:return
        }
        """;
    }

    [Test]
    public void StepOverRecursiveAwaitStaysInTheSameActivationTest() {
        var threadId = LaunchToMarker("marker:recurse");
        Assert.That(Evaluate("n", threadId).Result, Is.EqualTo("3"), "The outermost activation reaches the await first");

        // Without the breakpoint the inner activations run through; the step must end in the activation it started in
        SetBreakpoints(Array.Empty<int>());
        var frame = StepOver(threadId);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:delay")));
        var stopped = ReceivedEvents.OfType<StoppedEvent>().Last();
        Assert.That(Evaluate("n", stopped.ThreadId!.Value).Result, Is.EqualTo("3"));
    }

    [Test]
    public void StepOverRecursiveAwaitInInnerActivationTest() {
        var threadId = LaunchToMarker("marker:recurse");
        Continue(threadId);
        var second = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(Evaluate("n", second.ThreadId!.Value).Result, Is.EqualTo("2"));

        SetBreakpoints(Array.Empty<int>());
        var frame = StepOver(second.ThreadId!.Value);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:delay")));
        var stopped = ReceivedEvents.OfType<StoppedEvent>().Last();
        Assert.That(Evaluate("n", stopped.ThreadId!.Value).Result, Is.EqualTo("2"));
    }
}
