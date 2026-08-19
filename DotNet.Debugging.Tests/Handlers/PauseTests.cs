using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class PauseTests : BaseDebugTestFixture {
    public PauseTests() : base(nameof(PauseTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        while (true) {
            Console.WriteLine("tick"); // marker:tick
            Console.Out.Flush();
            Thread.Sleep(50);
        }
        """;
    }

    /// <summary>
    /// A pause that succeeds has to have stopped the program. This covers the moment right after
    /// attaching, where ICorDebug reports the process as not running while the debuggee is plainly still
    /// executing, and the stop was skipped and reported as done anyway. The refusal is transient, so a
    /// client that wants the pause asks again - as this does.
    /// </summary>
    [Test]
    public void PauseImmediatelyAfterAttachStopsTheProgramTest() {
        using var debuggee = StartDebuggee();

        Attach(debuggee.Id);
        ConfigurationDone();

        // The attach is still being made when 'ConfigurationDone' returns, so this waits for the
        // debuggee's threads to appear - the point at which a client can do anything with it, and the
        // earliest a pause is a fair thing to ask for.
        var threadId = WaitForFirstThread();

        PauseUntilAccepted(threadId);

        // What the debuggee wrote before it stopped is still travelling through the pipe, so silence is
        // required of a window that starts after that has landed rather than of the first instant.
        debuggee.CountPrintedDuring(TimeSpan.FromMilliseconds(500));

        Assert.That(debuggee.CountPrintedDuring(TimeSpan.FromSeconds(1)), Is.Zero,
            "The debuggee kept printing after a pause the adapter reported as successful");
    }

    /// <summary>
    /// Pausing a program that is already stopped is refused rather than answered with a success that
    /// stands for nothing. It leaves the stop it was asked to interrupt untouched.
    /// </summary>
    [Test]
    public void PauseWhileStoppedAtBreakpointIsRefusedTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:tick"));
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);

        var refusal = Assert.Throws<ProtocolException>(
            () => Host.SendRequestSync(new PauseRequest() { ThreadId = stopped.ThreadId!.Value }));
        Assert.That(refusal!.Message, Does.Contain("not running"));

        // Still stopped where it was, and still able to carry on from there
        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:tick")));

        Continue(stopped.ThreadId!.Value);
        WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
    }

    /// <summary>
    /// Retries a pause for as long as the process reports itself unstoppable, which is what a client
    /// that means to pause does with the refusal.
    /// </summary>
    private void PauseUntilAccepted(int threadId) {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true) {
            try {
                Host.SendRequestSync(new PauseRequest() { ThreadId = threadId });
                return;
            }
            catch (ProtocolException) when (DateTime.UtcNow < deadline) {
                System.Threading.Thread.Sleep(25);
            }
        }
    }

    private int WaitForFirstThread() {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline) {
            var threads = Host.SendRequestSync(new ThreadsRequest()).Threads;
            if (threads.Count > 0)
                return threads[0].Id;

            System.Threading.Thread.Sleep(25);
        }
        throw new TimeoutException("The attach never produced any threads");
    }
}
