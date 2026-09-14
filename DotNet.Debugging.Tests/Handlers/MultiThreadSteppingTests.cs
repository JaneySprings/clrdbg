using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Several threads run the same code while one of them is stepped: a step has to complete on the thread it was
// started on, and a breakpoint another thread hits meanwhile is reported as that thread's breakpoint stop
public class MultiThreadSteppingTests : BaseDebugTestFixture {
    public MultiThreadSteppingTests() : base(nameof(MultiThreadSteppingTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        using System.Diagnostics;

        var gate = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var totals = new long[4];
        var stopwatch = Stopwatch.StartNew();
        var workers = new List<Thread>();
        for (var index = 0; index < 4; index++) {
            var workerIndex = index;
            var worker = new Thread(() => Work(workerIndex)) { Name = $"Worker {workerIndex}", IsBackground = true };
            workers.Add(worker);
            worker.Start();
        }
        var waiter = new Thread(WaitForRelease) { Name = "Waiter", IsBackground = true };
        waiter.Start();
        gate.Set();
        foreach (var worker in workers)
            worker.Join();
        Console.WriteLine(totals.Sum()); // marker:end

        void Work(int index) {
            gate.Wait();
            var iteration = 0;
            while (stopwatch.ElapsedMilliseconds < 20000) {
                var value = Compute(index, iteration); // marker:loop
                totals[index] += value; // marker:after
                iteration = Advance(iteration); // marker:advance
                if (index == 0 && iteration == 1) {
                    release.Set(); // marker:release
                    totals[index] += 1; // marker:afterRelease
                }
            }
        }
        int Compute(int index, int iteration) { // marker:computeEntry
            var product = index * iteration; // marker:computeBody
            return product % 7;
        }
        int Advance(int iteration) {
            return iteration + 1;
        }
        void WaitForRelease() {
            release.Wait();
            totals[3] += 1000; // marker:released
        }
        """;
    }

    // A step whose stop reason and thread are checked by the test itself, unlike the fixture's helpers
    private StoppedEvent StepOverAny(int threadId) {
        Host.SendRequestSync(new NextRequest() { ThreadId = threadId });
        return WaitForStopped();
    }
    private StoppedEvent StepInAny(int threadId) {
        Host.SendRequestSync(new StepInRequest() { ThreadId = threadId });
        return WaitForStopped();
    }
    private StoppedEvent StepOutAny(int threadId) {
        Host.SendRequestSync(new StepOutRequest() { ThreadId = threadId });
        return WaitForStopped();
    }
    private static void AssertStepStop(StoppedEvent stopped, int threadId, string what) {
        Assert.That(stopped.Reason, Is.EqualTo(StoppedEvent.ReasonValue.Step), what);
        Assert.That(stopped.ThreadId, Is.EqualTo(threadId), $"{what}: the step must complete on the thread it was started on");
    }

    // With no breakpoints left, a sequence of steps must follow the stepped thread through its loop while the
    // other three threads run the same statements at full speed
    [Test]
    public void StepsStayOnTheSteppedThreadTest() {
        var threadId = LaunchToMarker("marker:loop");
        SetBreakpoints(Array.Empty<int>());
        var loopLine = GetMarkerLine("marker:loop");
        var afterLine = GetMarkerLine("marker:after");
        var advanceLine = GetMarkerLine("marker:advance");
        var computeEntryLine = GetMarkerLine("marker:computeEntry");
        var computeLine = GetMarkerLine("marker:computeBody");
        var index = Evaluate("index", threadId).Result;

        for (var round = 0; round < 5; round++) {
            var stopped = StepOverAny(threadId);
            AssertStepStop(stopped, threadId, $"round {round}: step over Compute");
            Assert.That(GetTopStackFrame(threadId).Line, Is.EqualTo(afterLine));

            stopped = StepOverAny(threadId);
            AssertStepStop(stopped, threadId, $"round {round}: step over the addition");
            Assert.That(GetTopStackFrame(threadId).Line, Is.EqualTo(advanceLine));

            stopped = StepInAny(threadId);
            AssertStepStop(stopped, threadId, $"round {round}: step into Advance");
            Assert.That(GetTopStackFrame(threadId).Name, Does.Contain("Advance"));

            stopped = StepOutAny(threadId);
            AssertStepStop(stopped, threadId, $"round {round}: step out of Advance");
            Assert.That(GetTopStackFrame(threadId).Name, Does.Contain("Work"));

            // The rest of the 'while' round trip: the assignment, the 'if', the loop condition, back to the call
            var frame = GetTopStackFrame(threadId);
            var guard = 0;
            while (frame.Line != loopLine) {
                stopped = StepOverAny(threadId);
                AssertStepStop(stopped, threadId, $"round {round}: step over towards the loop head (at line {frame.Line})");
                frame = GetTopStackFrame(threadId);
                Assert.That(++guard, Is.LessThan(10), "The loop head was never reached again");
            }

            // A step into a method lands on its opening brace, the body is one more step away
            stopped = StepInAny(threadId);
            AssertStepStop(stopped, threadId, $"round {round}: step into Compute");
            Assert.That(GetTopStackFrame(threadId).Line, Is.EqualTo(computeEntryLine));
            stopped = StepOverAny(threadId);
            AssertStepStop(stopped, threadId, $"round {round}: step to the body of Compute");
            Assert.That(GetTopStackFrame(threadId).Line, Is.EqualTo(computeLine));
            Assert.That(Evaluate("index", threadId).Result, Is.EqualTo(index), "The callee frame belongs to the stepped thread");

            stopped = StepOutAny(threadId);
            AssertStepStop(stopped, threadId, $"round {round}: step out of Compute");
            Assert.That(GetTopStackFrame(threadId).Line, Is.EqualTo(loopLine));

            stopped = StepOverAny(threadId);
            AssertStepStop(stopped, threadId, $"round {round}: finish the call statement");
            Assert.That(GetTopStackFrame(threadId).Line, Is.EqualTo(afterLine));
            // Back to the loop head for the next round
            frame = GetTopStackFrame(threadId);
            guard = 0;
            while (frame.Line != loopLine) {
                stopped = StepOverAny(threadId);
                AssertStepStop(stopped, threadId, $"round {round}: step over back to the loop head (at line {frame.Line})");
                frame = GetTopStackFrame(threadId);
                Assert.That(++guard, Is.LessThan(10), "The loop head was never reached again");
            }
        }
    }

    // A breakpoint every thread hits stays set while one thread steps over: whichever comes first, the stepped
    // thread reaching its next line or another thread hitting the breakpoint, is reported on the thread it happened to
    [Test]
    public void BreakpointOnAnotherThreadInterruptsTheStepTest() {
        var threadId = LaunchToMarker("marker:loop");
        var loopLine = GetMarkerLine("marker:loop");
        var afterLine = GetMarkerLine("marker:after");

        var interruptions = 0;
        for (var round = 0; round < 20; round++) {
            var stopped = StepOverAny(threadId);
            var stoppedThreadId = stopped.ThreadId!.Value;
            var frame = GetTopStackFrame(stoppedThreadId);
            if (stopped.Reason == StoppedEvent.ReasonValue.Step) {
                Assert.That(stoppedThreadId, Is.EqualTo(threadId), $"round {round}: a step stop belongs to the stepped thread");
                Assert.That(frame.Line, Is.EqualTo(afterLine));
                // Back to the loop head to step over the call again
                Continue(threadId);
                stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
                threadId = stopped.ThreadId!.Value;
            }
            else {
                Assert.That(stopped.Reason, Is.EqualTo(StoppedEvent.ReasonValue.Breakpoint), $"round {round}: {stopped.Reason}");
                Assert.That(stoppedThreadId, Is.Not.EqualTo(threadId), $"round {round}: the stepped thread cannot hit the breakpoint before its step completes");
                Assert.That(frame.Line, Is.EqualTo(loopLine), $"round {round}: the interrupting thread stands at the breakpoint");
                interruptions++;
                threadId = stoppedThreadId;
            }
        }
        TestContext.Out.WriteLine($"Steps interrupted by another thread's breakpoint: {interruptions} of 20");
    }

    // After another thread's breakpoint took over, the abandoned step must not fire later: the breakpoint is removed
    // and the new thread is stepped, no stop of the original thread may sneak in
    [Test]
    public void AbandonedStepDoesNotFireLaterTest() {
        var threadId = LaunchToMarker("marker:loop");
        var loopLine = GetMarkerLine("marker:loop");
        var afterLine = GetMarkerLine("marker:after");

        StoppedEvent stopped;
        var round = 0;
        while (true) {
            stopped = StepOverAny(threadId);
            if (stopped.Reason == StoppedEvent.ReasonValue.Breakpoint)
                break;
            AssertStepStop(stopped, threadId, $"round {round}");
            Continue(threadId);
            stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
            threadId = stopped.ThreadId!.Value;
            Assert.That(++round, Is.LessThan(50), "No other thread ever interrupted a step");
        }
        var interruptingThreadId = stopped.ThreadId!.Value;
        Assert.That(interruptingThreadId, Is.Not.EqualTo(threadId));
        Assert.That(GetTopStackFrame(interruptingThreadId).Line, Is.EqualTo(loopLine));

        SetBreakpoints(Array.Empty<int>());
        for (var i = 0; i < 6; i++) {
            stopped = StepOverAny(interruptingThreadId);
            AssertStepStop(stopped, interruptingThreadId, $"step {i} of the interrupting thread");
        }
        // The original thread keeps running: it is nowhere near where it was left
        Continue(interruptingThreadId);
        var stopEvent = WaitForEvent<DebugEvent>(it => it is StoppedEvent or TerminatedEvent, timeoutMs: 40000);
        Assert.That(stopEvent, Is.InstanceOf<TerminatedEvent>(), "No stop may be reported once the step was abandoned");
    }

    // Thread 0 releases a waiting thread inside the statement it steps over, and the released thread hits a breakpoint
    // in the same instant the step completes. Both have to be reported, in either order: the step stop on thread 0
    // and the breakpoint stop on the released thread, neither may be swallowed
    [Test]
    public void SimultaneousStepCompletionAndBreakpointTest() {
        var releaseLine = GetMarkerLine("marker:release");
        var afterReleaseLine = GetMarkerLine("marker:afterRelease");
        var releasedLine = GetMarkerLine("marker:released");
        var swallowed = new List<int>();
        var stepFirst = 0;
        var breakpointFirst = 0;

        for (var round = 0; round < 15; round++) {
            Launch();
            SetBreakpoints(releaseLine, releasedLine);
            ConfigurationDone();
            var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
            var threadId = stopped.ThreadId!.Value;
            Assert.That(GetTopStackFrame(threadId).Line, Is.EqualTo(releaseLine));
            SetBreakpoints(releasedLine);

            var first = StepOverAny(threadId);
            StoppedEvent second;
            if (first.Reason == StoppedEvent.ReasonValue.Step) {
                stepFirst++;
                Assert.That(first.ThreadId, Is.EqualTo(threadId), $"round {round}");
                Assert.That(GetTopStackFrame(threadId).Line, Is.EqualTo(afterReleaseLine), $"round {round}");
                Continue(threadId);
                // The released thread reaches its breakpoint within milliseconds, or ran through it already
                try {
                    second = WaitForEvent<StoppedEvent>(timeoutMs: 3000);
                }
                catch (TimeoutException) {
                    swallowed.Add(round);
                    TearDownRound();
                    continue;
                }
                Assert.That(second.Reason, Is.EqualTo(StoppedEvent.ReasonValue.Breakpoint), $"round {round}");
                Assert.That(second.ThreadId, Is.Not.EqualTo(threadId), $"round {round}: the breakpoint belongs to the released thread");
                Assert.That(GetTopStackFrame(second.ThreadId!.Value).Line, Is.EqualTo(releasedLine), $"round {round}");
            }
            else {
                breakpointFirst++;
                Assert.That(first.Reason, Is.EqualTo(StoppedEvent.ReasonValue.Breakpoint), $"round {round}");
                Assert.That(first.ThreadId, Is.Not.EqualTo(threadId), $"round {round}: the breakpoint belongs to the released thread");
                Assert.That(GetTopStackFrame(first.ThreadId!.Value).Line, Is.EqualTo(releasedLine), $"round {round}");
                // The interrupted step is abandoned: the stepped thread runs on freely
                second = first;
            }
            // The released thread is stepped next: its step, and nothing left over from the interrupted one, must report
            var releasedThreadId = second.ThreadId!.Value;
            var third = StepOverAny(releasedThreadId);
            AssertStepStop(third, releasedThreadId, $"round {round}: step over on the released thread");
            Assert.That(GetTopStackFrame(releasedThreadId).Line, Is.EqualTo(releasedLine + 1), $"round {round}: the released thread stepped to its closing brace");
            TearDownRound();
        }
        TestContext.Out.WriteLine($"Step reported first: {stepFirst}, breakpoint reported first: {breakpointFirst}");
        Assert.That(swallowed, Is.Empty, "The released thread ran through its breakpoint without a stop in these rounds");
    }

    // Each round of the simultaneous test is a fresh session: the fixture's TearDown/SetUp pair is replayed by hand
    private void TearDownRound() {
        TearDown();
        SetUp();
    }
}
