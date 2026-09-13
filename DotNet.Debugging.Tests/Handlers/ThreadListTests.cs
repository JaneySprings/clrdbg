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
        var threads = GetThreadsWithStacks();

        Assert.That(threads.Keys.Single(it => it.Id == threadId).Name, Is.EqualTo("Main Thread"), "The thread stopped in Main is the main thread");
        Assert.That(threads.Keys.Count(it => it.Name == "Main Thread"), Is.EqualTo(1));
        foreach (var (thread, frames) in threads)
            Assert.That(frames, Is.Not.Empty, $"Thread {thread.Id} '{thread.Name}' is listed without any frame to show");

        var workers = threads.Where(it => it.Value.Any(frame => frame.Name.Contains("Park"))).Select(it => it.Key).ToList();
        Assert.That(workers, Has.Count.EqualTo(2), "Both parked workers are listed");
        Assert.That(workers.Select(it => it.Name), Does.Contain("background-io"), "The managed thread name is shown");
        Assert.That(workers.Select(it => it.Name), Does.Not.Contain("Main Thread"));
    }

    [Test]
    public void ExitedWorkersAreNotListedTest() {
        var threadId = LaunchToMarker("marker:joined");
        var threads = GetThreadsWithStacks();

        Assert.That(threads.Keys.Single(it => it.Id == threadId).Name, Is.EqualTo("Main Thread"));
        Assert.That(threads.Keys.Select(it => it.Name), Does.Not.Contain("background-io"));
        Assert.That(threads.Any(it => it.Value.Any(frame => frame.Name.Contains("Park"))), Is.False, "The joined workers are gone");
        foreach (var (thread, frames) in threads)
            Assert.That(frames, Is.Not.Empty, $"Thread {thread.Id} '{thread.Name}' is listed without any frame to show");
    }

    // The OS names of unnamed threads differ per platform (Linux reports the process name), so the workers are told
    // apart by the frames they are parked in
    private Dictionary<Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Thread, List<StackFrame>> GetThreadsWithStacks() {
        var threads = Host.SendRequestSync(new ThreadsRequest()).Threads;
        return threads.ToDictionary(it => it, it => Host.SendRequestSync(new StackTraceRequest() { ThreadId = it.Id }).StackFrames);
    }
}
