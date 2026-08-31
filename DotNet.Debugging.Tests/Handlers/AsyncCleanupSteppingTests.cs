using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Cleanup regions in async methods. A 'using' with an await in its body puts the await's yield/resume
// machinery and the hidden finally into the same hidden span, told apart only by handler containment;
// 'await using' and 'await foreach' hoist their DisposeAsync out of any handler and await it there
public class AsyncCleanupSteppingTests : BaseDebugTestFixture {
    public AsyncCleanupSteppingTests() : base(nameof(AsyncCleanupSteppingTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        await UsingWithAwaitBody();
        await AwaitUsing();
        await AwaitForeach();
        await AwaitForeachBreak();
        Console.WriteLine("done"); // marker:done

        static async Task UsingWithAwaitBody() {
            using (var resource = new SyncResource())
                await Task.Delay(20); // marker:awaitBody
            Console.WriteLine("after sync using"); // marker:afterAwaitBody
        }

        static async Task AwaitUsing() {
            await using (var resource = new AsyncResource())
                Console.WriteLine("await-using body"); // marker:asyncUsingBody
            Console.WriteLine("after await-using"); // marker:afterAsyncUsing
        }

        static async Task AwaitForeach() {
            await foreach (var item in ItemsAsync()) // marker:asyncForeachHeader
                Console.WriteLine(item); // marker:asyncForeachBody
            Console.WriteLine("after await-foreach"); // marker:afterAsyncForeach
        }

        static async IAsyncEnumerable<int> ItemsAsync() {
            await Task.Delay(20);
            yield return 1;
        }

        static async Task AwaitForeachBreak() {
            await foreach (var item in GuardedItemsAsync()) {
                Console.WriteLine(item);
                break; // marker:asyncForeachBreak
            }
            Console.WriteLine("after break"); // marker:afterAsyncForeachBreak
        }

        // The await in the finally makes the enumerator's DisposeAsync really yield when the loop breaks
        static async IAsyncEnumerable<int> GuardedItemsAsync() {
            try {
                await Task.Delay(20);
                yield return 1;
                yield return 2;
            }
            finally {
                await Task.Delay(20);
            }
        }

        sealed class SyncResource : IDisposable {
            public void Dispose() => Console.WriteLine("sync dispose");
        }

        sealed class AsyncResource : IAsyncDisposable {
            public async ValueTask DisposeAsync() {
                await Task.Delay(20);
                Console.WriteLine("async dispose"); // marker:asyncDispose
            }
        }
        """;
    }

    // The await machinery and the hidden finally of the using share one hidden span: the step is carried
    // across the yield, then passes through the finally that disposes the resource, onto the next statement
    [Test]
    public void StepOverAwaitInsideUsingBodyReachesTheNextStatement() {
        var threadId = LaunchToMarker("marker:awaitBody");
        Host.SendRequestSync(new NextRequest() { ThreadId = threadId });
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Step);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:afterAwaitBody")));
    }

    // The hidden DisposeAsync of an 'await using' yields, the step must be carried across it
    [Test]
    public void StepOverAwaitUsingBodyReachesTheNextStatement() {
        var threadId = LaunchToMarker("marker:asyncUsingBody");
        Host.SendRequestSync(new NextRequest() { ThreadId = threadId });
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Step);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:afterAsyncUsing")));
    }

    // A 'break' jumps over the loop's own MoveNextAsync await straight to the hidden DisposeAsync, whose
    // await yields - the step must be carried by whichever await control flow reaches, not the next in
    // IL order
    [Test]
    public void StepOverBreakInAwaitForeachReachesTheNextStatement() {
        var threadId = LaunchToMarker("marker:asyncForeachBreak");
        Host.SendRequestSync(new NextRequest() { ThreadId = threadId });
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Step);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:afterAsyncForeachBreak")));
    }

    // The last loop hop awaits twice inside one step: MoveNextAsync reports the end of the sequence and
    // the enumerator's DisposeAsync yields after it - the resumed step must arm the second await again
    [Test]
    public void StepOverLastAwaitForeachIterationReachesTheNextStatement() {
        var threadId = LaunchToMarker("marker:asyncForeachBody");
        Host.SendRequestSync(new NextRequest() { ThreadId = threadId });
        var header = WaitForStopped(StoppedEvent.ReasonValue.Step);
        Assert.That(GetTopStackFrame(header.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:asyncForeachHeader")));

        Host.SendRequestSync(new NextRequest() { ThreadId = header.ThreadId!.Value });
        var next = WaitForStopped(StoppedEvent.ReasonValue.Step);
        Assert.That(GetTopStackFrame(next.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:afterAsyncForeach")));
    }
}
