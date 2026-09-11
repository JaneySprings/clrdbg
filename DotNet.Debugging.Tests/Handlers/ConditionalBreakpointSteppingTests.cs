using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A step has to survive a breakpoint that evaluates without stopping (a false condition, a logpoint), whether the
// breakpoint sits inside the stepped-over call on the stepping thread or other threads keep hitting it meanwhile
public class ConditionalBreakpointSteppingTests : BaseDebugTestFixture {
    public ConditionalBreakpointSteppingTests() : base(nameof(ConditionalBreakpointSteppingTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var gate = new ManualResetEventSlim(false);
        var workers = new List<Thread>();
        for (var i = 0; i < 2; i++) {
            var worker = new Thread(Work) { IsBackground = true };
            workers.Add(worker);
            worker.Start();
        }
        var sum = Add(1, 2); // marker:first
        gate.Set(); // marker:second
        Thread.Sleep(1500); // marker:third
        Console.WriteLine(sum); // marker:end

        void Work() {
            gate.Wait();
            var count = 0;
            while (count < 3_000_000) {
                count = Tick(count); // marker:loop
            }
        }
        static int Add(int left, int right) {
            return left + right; // marker:add
        }
        static int Tick(int count) {
            return count + 1;
        }

        static class Helper {
            public static int Twice(int value) {
                return value * 2;
            }
        }
        """;
    }

    private StoppedEvent StepOverAny(int threadId) {
        Host.SendRequestSync(new NextRequest() { ThreadId = threadId });
        var next = WaitForEvent<DebugEvent>(it => it is StoppedEvent or TerminatedEvent);
        Assert.That(next, Is.InstanceOf<StoppedEvent>(), "The step was lost, the program ran to its end");
        return (StoppedEvent)next;
    }
    private void AssertStepStop(StoppedEvent stopped, int threadId, string marker) {
        Assert.That(stopped.Reason, Is.EqualTo(StoppedEvent.ReasonValue.Step));
        Assert.That(stopped.ThreadId, Is.EqualTo(threadId), "The step must complete on the thread it was started on");
        Assert.That(GetTopStackFrame(threadId).Line, Is.EqualTo(GetMarkerLine(marker)));
    }

    // The condition is decided by the debugger alone, no code runs in the debuggee
    [Test]
    public void StepOverSurvivesAFalseConditionInsideTheCallTest() {
        var threadId = LaunchToMarker("marker:first");
        SetBreakpoints(new SourceBreakpoint() { Line = GetMarkerLine("marker:add"), Condition = "left == 100" });
        AssertStepStop(StepOverAny(threadId), threadId, "marker:second");
    }

    // The condition calls into the debuggee, an evaluation runs on the stepping thread itself
    [Test]
    public void StepOverSurvivesAFalseConditionEvaluatedInsideTheCallTest() {
        var threadId = LaunchToMarker("marker:first");
        SetBreakpoints(new SourceBreakpoint() { Line = GetMarkerLine("marker:add"), Condition = "Helper.Twice(left) == 100" });
        AssertStepStop(StepOverAny(threadId), threadId, "marker:second");
    }

    [Test]
    public void StepOverSurvivesALogpointInsideTheCallTest() {
        var threadId = LaunchToMarker("marker:first");
        SetBreakpoints(new SourceBreakpoint() { Line = GetMarkerLine("marker:add"), LogMessage = "left is {left}" });
        AssertStepStop(StepOverAny(threadId), threadId, "marker:second");
        var logpoint = ReceivedEvents.OfType<OutputEvent>().FirstOrDefault(it => it.Output.Contains("left is 1"));
        Assert.That(logpoint, Is.Not.Null, "The logpoint printed on the way");
    }

    // Two workers hit a false-condition breakpoint thousands of times while the main thread steps
    [Test]
    public void StepSurvivesOtherThreadsFalseConditionsTest() {
        var threadId = LaunchToMarker("marker:second");
        SetBreakpoints(new SourceBreakpoint() { Line = GetMarkerLine("marker:loop"), Condition = "count == -1" });
        AssertStepStop(StepOverAny(threadId), threadId, "marker:third");
    }

    // The workers' conditions call into the debuggee: the main thread's step completes while such an evaluation runs
    [Test]
    public void StepSurvivesOtherThreadsFalseConditionsEvaluatedTest() {
        var threadId = LaunchToMarker("marker:second");
        SetBreakpoints(new SourceBreakpoint() { Line = GetMarkerLine("marker:loop"), Condition = "Helper.Twice(count) == -1" });
        AssertStepStop(StepOverAny(threadId), threadId, "marker:third");
        // The held thread runs again: the program finishes once the breakpoint is gone
        SetBreakpoints(Array.Empty<int>());
        Continue(threadId);
        WaitForEvent<TerminatedEvent>();
    }

    // A true condition on another thread still wins over the step, as any stopping breakpoint does; when the step
    // completes first, the breakpoint is the next stop
    [Test]
    public void OtherThreadsTrueConditionInterruptsTheStepTest() {
        var threadId = LaunchToMarker("marker:second");
        SetBreakpoints(new SourceBreakpoint() { Line = GetMarkerLine("marker:loop"), Condition = "count == 5" });
        var stopped = StepOverAny(threadId);
        if (stopped.Reason == StoppedEvent.ReasonValue.Step) {
            AssertStepStop(stopped, threadId, "marker:third");
            Continue(threadId);
            stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        }
        Assert.That(stopped.Reason, Is.EqualTo(StoppedEvent.ReasonValue.Breakpoint));
        Assert.That(stopped.ThreadId, Is.Not.EqualTo(threadId));
        Assert.That(Evaluate("count", stopped.ThreadId!.Value).Result, Is.EqualTo("5"));
    }
}
