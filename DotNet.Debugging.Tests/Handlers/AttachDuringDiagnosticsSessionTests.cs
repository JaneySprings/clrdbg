using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class AttachDuringDiagnosticsSessionTests : BaseDebugTestFixture {
    public AttachDuringDiagnosticsSessionTests() : base(nameof(AttachDuringDiagnosticsSessionTests)) { }

    // The runtime enables an EventSource on its diagnostics server thread, the one that answers every diagnostics
    // command. The source holds that thread in managed code for a while, the way enabling the runtime counters does
    protected override string CreateProgramFileContent() {
        return """
        using System.Diagnostics.Tracing;

        var source = new SlowEventSource();
        while (true) {
            Console.WriteLine("tick");
            Console.Out.Flush();
            Thread.Sleep(50);
        }

        [EventSource(Name = "SlowEventSource")]
        sealed class SlowEventSource : EventSource {
            protected override void OnEventCommand(EventCommandEventArgs command) {
                if (command.Command != EventCommand.Enable)
                    return;
                Console.Error.WriteLine("enabling");
                Console.Error.Flush();
                Thread.Sleep(3000);
            }
        }
        """;
    }

    [Test]
    public void AttachCompletesWhileTheDiagnosticsServerIsBusyTest() {
        using var debuggee = StartDebuggee();
        var client = new DiagnosticsClient(debuggee.Id);
        var session = Task.Run(() => client.StartEventPipeSession(new EventPipeProvider("SlowEventSource", EventLevel.Informational)));
        // The session is being enabled by now: the diagnostics server thread sits in the source's callback
        System.Threading.Thread.Sleep(1000);
        Assert.That(session.IsCompleted, Is.False, "The session must still be starting when the debugger attaches");

        Attach(debuggee.Id);
        var configurationDone = Task.Run(ConfigurationDone);

        Assert.That(configurationDone.Wait(TimeSpan.FromSeconds(20)), "The attach must not wait for the diagnostics server");
        Assert.That(session.Wait(TimeSpan.FromSeconds(20)), "The diagnostics session must start once the attach is continued");
        session.Result.Dispose();
        WaitForFirstThread();
        Assert.That(debuggee.CountPrintedDuring(TimeSpan.FromSeconds(1)), Is.GreaterThan(0), "The debuggee must keep running");
    }
}
