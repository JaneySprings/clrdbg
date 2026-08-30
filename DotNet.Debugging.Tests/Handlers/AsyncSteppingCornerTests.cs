using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class AsyncSteppingCornerTests : BaseDebugTestFixture {
    public AsyncSteppingCornerTests() : base(nameof(AsyncSteppingCornerTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var completed = await CompletedAsync(); // marker:completedCall
        var looped = await LoopAsync();
        var eager = await EagerAsync(); // marker:eagerCall
        var value = await ValueTaskAsync(); // marker:valueTaskCall
        FireAndForget(); // marker:fireCall
        var nested = await OuterAsync(); // marker:outerCall
        var caught = await CatchAsync();
        var results = await Task.WhenAll(WorkerAsync(1, 40), WorkerAsync(2, 60));
        Console.WriteLine(completed + looped + eager + value + nested + caught + results.Length); // marker:end

        static async Task<int> CompletedAsync() {
            var seed = 1; // marker:beforeCompletedAwait
            await Task.CompletedTask; // marker:completedAwait
            return seed; // marker:afterCompletedAwait
        }
        static async Task<int> LoopAsync() {
            var total = 0;
            for (var i = 0; i < 3; i++) {
                await Task.Delay(10); // marker:loopAwait
                total += i; // marker:loopBody
            }
            return total;
        }
        static async Task<int> EagerAsync() {
            var seed = 3; // marker:beforeFirstAwait
            await Task.Delay(10);
            return seed;
        }
        static async ValueTask<int> ValueTaskAsync() {
            await Task.Delay(10); // marker:valueTaskAwait
            return 2; // marker:valueTaskReturn
        }
        static async void FireAndForget() {
            var pending = 1; // marker:insideVoid
            await Task.Delay(10);
            Console.WriteLine(pending);
        }
        static async Task<int> OuterAsync() {
            var result = await InnerAsync(); // marker:insideOuter
            return result + 1;
        }
        static async Task<int> InnerAsync() {
            await Task.Delay(10);
            return 5; // marker:innerReturn
        }
        static async Task<int> CatchAsync() {
            try {
                await ThrowAsync(); // marker:faultingAwait
                return 0;
            }
            catch (InvalidOperationException) { // marker:catchClause
                return 7; // marker:catchBody
            }
        }
        static async Task<int> ThrowAsync() {
            await Task.Delay(10);
            throw new InvalidOperationException("async boom");
        }
        static async Task<int> WorkerAsync(int id, int delay) {
            var start = id; // marker:workerStart
            await Task.Delay(delay); // marker:workerAwait
            return start + 100; // marker:workerReturn
        }
        """;
    }

    // An await of an already completed task never yields, the plain stepper finishes the step
    [Test]
    public void StepOverCompletedAwaitTest() {
        var threadId = LaunchToMarker("marker:completedAwait");
        var frame = StepOver(threadId);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:afterCompletedAwait")));
    }

    // The same yield and resume offsets carry a fresh async step on every iteration
    [Test]
    public void StepOverAwaitInLoopTest() {
        var threadId = LaunchToMarker("marker:loopAwait");
        var frame = StepOver(threadId);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:loopBody")));

        Continue(threadId);
        var secondHit = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var secondFrame = StepOver(secondHit.ThreadId!.Value);
        Assert.That(secondFrame.Line, Is.EqualTo(GetMarkerLine("marker:loopBody")));
        Assert.That(Evaluate("i", secondHit.ThreadId!.Value).Result, Is.EqualTo("1"), "The second step happened in the second iteration");
    }

    // Two chained async step-outs: the inner method's task completes into the outer, the outer's into Main.
    // Each step is issued on the thread of the latest stop, the resume may have moved to another one
    [Test]
    public void StepOutOfNestedAsyncMethodsTest() {
        var threadId = LaunchToMarker("marker:innerReturn");
        Host.SendRequestSync(new StepOutRequest() { ThreadId = threadId });
        var outerStop = WaitForStopped(StoppedEvent.ReasonValue.Step);
        var outerFrame = GetTopStackFrame(outerStop.ThreadId!.Value);
        Assert.That(outerFrame.Name, Does.Contain("OuterAsync"));
        Assert.That(outerFrame.Line, Is.EqualTo(GetMarkerLine("marker:insideOuter")));

        var mainFrame = StepOut(outerStop.ThreadId!.Value);
        Assert.That(mainFrame.Name, Does.Contain("Main"));
        Assert.That(mainFrame.Line, Is.EqualTo(GetMarkerLine("marker:outerCall")));
    }

    // Step out before the method has yielded for the first time: the stop still waits for the task
    [Test]
    public void StepOutBeforeFirstAwaitTest() {
        var threadId = LaunchToMarker("marker:beforeFirstAwait");
        var frame = StepOut(threadId);
        Assert.That(frame.Name, Does.Contain("Main"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:eagerCall")));
    }

    // An async void method has no task to wait for; before its first yield a plain step out reaches the caller
    [Test]
    public void StepOutOfAsyncVoidBeforeAwaitTest() {
        var threadId = LaunchToMarker("marker:insideVoid");
        var frame = StepOut(threadId);
        Assert.That(frame.Name, Does.Contain("Main"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:fireCall")));
    }

    // A ValueTask method uses AsyncValueTaskMethodBuilder, whose debugger contract must work the same
    [Test]
    public void StepOverValueTaskAwaitTest() {
        var threadId = LaunchToMarker("marker:valueTaskAwait");
        var frame = StepOver(threadId);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:valueTaskReturn")));
    }

    [Test]
    public void StepOutOfValueTaskAsyncMethodTest() {
        var threadId = LaunchToMarker("marker:valueTaskReturn");
        var frame = StepOut(threadId);
        Assert.That(frame.Name, Does.Contain("Main"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:valueTaskCall")));
    }

    // The awaited task faults: the method resumes at the await only to rethrow, the step lands in the catch
    [Test]
    public void StepOverFaultingAwaitLandsInCatchTest() {
        var threadId = LaunchToMarker("marker:faultingAwait");
        var frame = StepOver(threadId);
        Assert.That(frame.Name, Does.Contain("CatchAsync"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:catchClause")).Or.EqualTo(GetMarkerLine("marker:catchBody")));
    }

    // An exception stop during a pending async step wins over the step and abandons it
    [Test]
    public void ExceptionDuringAsyncStepTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:faultingAwait"));
        SetExceptionBreakpoints(new[] { "all" }, ("all", null));
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);

        Host.SendRequestSync(new NextRequest() { ThreadId = stopped.ThreadId!.Value });
        var next = WaitForStopped();
        Assert.That(next.Reason, Is.EqualTo(StoppedEvent.ReasonValue.Exception), "The throw inside the awaited task stops before the step completes");

        Continue(next.ThreadId!.Value);
        CollectStopsUntilExit();
    }

    // Two invocations of the same method run concurrently; the resume breakpoint is shared, the async id
    // of the builder tells the stepped invocation apart from the other one passing through it
    [Test]
    public void ConcurrentInvocationsKeepTheStepTest() {
        Launch();
        SetBreakpoints(new SourceBreakpoint() {
            Line = GetMarkerLine("marker:workerAwait"),
            Condition = "id == 2",
        });
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(Evaluate("id", stopped.ThreadId!.Value).Result, Is.EqualTo("2"));

        Host.SendRequestSync(new NextRequest() { ThreadId = stopped.ThreadId!.Value });
        var stepStop = WaitForStopped(StoppedEvent.ReasonValue.Step);
        Assert.That(GetTopStackFrame(stepStop.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:workerReturn")));
        Assert.That(Evaluate("start", stepStop.ThreadId!.Value).Result, Is.EqualTo("2"), "The step stayed with the invocation it was started in");
    }
}
