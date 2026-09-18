using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// An attach announces the threads that exist already in the runtime's own order, which need not begin with the main
// one: the main thread is told by its id there (the process id on Linux, the lowest id on macOS, the first on Windows).
// A local runtime happens to announce the main thread first, so this guards the rule against naming a worker here;
// the order that needs the rule is a remote runtime's
public class AttachedThreadListTests : BaseDebugTestFixture {
    public AttachedThreadListTests() : base(nameof(AttachedThreadListTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var parked = new CountdownEvent(2);
        var release = new ManualResetEventSlim(false);
        var named = new Thread(Park) { Name = "early-worker", IsBackground = true };
        var unnamed = new Thread(Park) { IsBackground = true };
        named.Start();
        unnamed.Start();
        parked.Wait();
        while (true) {
            Console.WriteLine("tick"); // marker:tick
            Console.Out.Flush();
            Thread.Sleep(50);
        }

        void Park() {
            parked.Signal();
            release.Wait();
        }
        """;
    }

    [Test]
    public void MainThreadIsNamedAfterAnAttachTest() {
        using var debuggee = StartDebuggee();
        // The workers exist before the debugger does
        debuggee.CountPrintedDuring(TimeSpan.FromMilliseconds(500));

        Attach(debuggee.Id);
        SetBreakpoints(GetMarkerLine("marker:tick"));
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);

        var threads = Host.SendRequestSync(new ThreadsRequest()).Threads;
        Assert.That(threads.Single(it => it.Id == stopped.ThreadId).Name, Is.EqualTo("Main Thread"), "The thread running Main is the main thread");
        Assert.That(threads.Count(it => it.Name == "Main Thread"), Is.EqualTo(1));
        Assert.That(threads.Select(it => it.Name), Does.Contain("early-worker"));
    }

    // A pause names a thread the client can show: asked for one that does not exist, it reports one with frames
    [Test]
    public void PauseAskedOfAnUnknownThreadReportsOneWithFramesTest() {
        using var debuggee = StartDebuggee();

        Attach(debuggee.Id);
        ConfigurationDone();
        WaitForFirstThread();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true) {
            try {
                Host.SendRequestSync(new PauseRequest() { ThreadId = 1 });
                break;
            }
            catch (Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.ProtocolException) when (DateTime.UtcNow < deadline) {
                System.Threading.Thread.Sleep(25);
            }
        }
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Pause);
        var frames = Host.SendRequestSync(new StackTraceRequest() { ThreadId = stopped.ThreadId!.Value }).StackFrames;
        Assert.That(frames, Is.Not.Empty, "The paused thread has a stack to show");
        Assert.That(Host.SendRequestSync(new ThreadsRequest()).Threads.Select(it => it.Id), Does.Contain(stopped.ThreadId!.Value), "The paused thread is a listed one");
    }
}
