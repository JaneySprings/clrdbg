using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class DetachTests : BaseDebugTestFixture {
    public DetachTests() : base(nameof(DetachTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        while (true) {
            Console.WriteLine("tick");
            Console.Out.Flush();
            Thread.Sleep(50);
        }
        """;
    }

    [Test]
    public void DetachLeavesTheDebuggeeRunningTest() {
        using var debuggee = StartDebuggee();

        Attach(debuggee.Id);
        ConfigurationDone();
        WaitForFirstThread();

        Host.SendRequestSync(new DisconnectRequest() { TerminateDebuggee = false });

        Assert.That(debuggee.CountPrintedDuring(TimeSpan.FromSeconds(1)), Is.GreaterThan(0),
            "The debuggee must keep running after a detach");
    }
}
