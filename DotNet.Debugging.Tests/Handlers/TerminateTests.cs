using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class TerminateTests : BaseDebugTestFixture {
    public TerminateTests() : base(nameof(TerminateTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        while (true) {
            Console.WriteLine("tick"); // marker:tick
            Console.Out.Flush();
            Thread.Sleep(50);
        }
        """;
    }

    [Test]
    public void TerminateWhileTheAttachedProgramRunsKillsItTest() {
        using var debuggee = StartDebuggee();

        Attach(debuggee.Id);
        ConfigurationDone();
        WaitForFirstThread();

        Host.SendRequestSync(new TerminateRequest());

        // What the debuggee wrote before it died is still travelling through the pipe, so the silence
        // is required of a window that starts after that has landed
        debuggee.CountPrintedDuring(TimeSpan.FromMilliseconds(500));

        Assert.That(debuggee.CountPrintedDuring(TimeSpan.FromSeconds(2)), Is.Zero,
            "The debuggee kept printing after a terminate the adapter reported as successful");
    }

    [Test]
    public void TerminateWhileTheAttachedProgramIsStoppedKillsItTest() {
        using var debuggee = StartDebuggee();

        Attach(debuggee.Id);
        SetBreakpoints(GetMarkerLine("marker:tick"));
        ConfigurationDone();

        WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);

        Host.SendRequestSync(new TerminateRequest());

        debuggee.CountPrintedDuring(TimeSpan.FromMilliseconds(500));

        Assert.That(debuggee.CountPrintedDuring(TimeSpan.FromSeconds(2)), Is.Zero,
            "The debuggee kept printing after a terminate the adapter reported as successful");
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
