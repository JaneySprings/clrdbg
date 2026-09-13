using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class ThreadListTests : BaseDebugTestFixture {
    public ThreadListTests() : base(nameof(ThreadListTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var pause = new ManualResetEventSlim(false);
        var ready = new CountdownEvent(2);
        var named = new Thread(Park) { Name = "background-io" };
        var unnamed = new Thread(Park);
        named.Start();
        unnamed.Start();
        ready.Wait();
        Console.WriteLine("workers parked"); // marker:stop
        pause.Set();
        named.Join();
        unnamed.Join();
        Console.WriteLine("joined"); // marker:joined

        void Park() {
            ready.Signal();
            pause.Wait();
        }
        """;
    }

    [Test]
    public void MainThreadIsTheOneRunningMainTest() {
        var threadId = LaunchToMarker("marker:stop");
        var threads = Host.SendRequestSync(new ThreadsRequest()).Threads;

        var stoppedThread = threads.Single(it => it.Id == threadId);
        Assert.That(stoppedThread.Name, Is.EqualTo("Main Thread"), "The thread stopped in Main is the main thread");
        Assert.That(threads.Count(it => it.Name == "Main Thread"), Is.EqualTo(1));
        Assert.That(threads.Select(it => it.Name), Does.Contain("background-io"));
        Assert.That(threads.Count(it => it.Name == "<No Name>"), Is.EqualTo(1), "The unnamed worker is the only nameless thread");
        foreach (var thread in threads) {
            var frames = Host.SendRequestSync(new StackTraceRequest() { ThreadId = thread.Id }).StackFrames;
            Assert.That(frames, Is.Not.Empty, $"Thread {thread.Id} '{thread.Name}' is listed without any frame to show");
        }
    }

    [Test]
    public void ExitedWorkersAreNotListedTest() {
        var threadId = LaunchToMarker("marker:joined");
        var threads = Host.SendRequestSync(new ThreadsRequest()).Threads;

        Assert.That(threads.Single(it => it.Id == threadId).Name, Is.EqualTo("Main Thread"));
        Assert.That(threads.Select(it => it.Name), Does.Not.Contain("background-io"));
        Assert.That(threads.Count(it => it.Name == "<No Name>"), Is.EqualTo(0), "Neither the exited worker nor a runtime thread is listed");
    }
}
