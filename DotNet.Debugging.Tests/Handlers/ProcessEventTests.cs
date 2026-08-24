using DotNet.Debugging.Adapter;
using DotNet.Debugging.Adapter.Terminal;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class ProcessEventTests : BaseDebugTestFixture {
    public ProcessEventTests() : base(nameof(ProcessEventTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        Console.WriteLine($"pid {Environment.ProcessId}");
        Console.Out.Flush();
        while (true) {
            Thread.Sleep(50);
        }
        """;
    }

    [Test]
    public void LaunchReportsTheProcessItStartedTest() {
        Launch();
        ConfigurationDone();

        var reported = WaitForEvent<ProcessEvent>();

        Assert.That(reported.SystemProcessId, Is.EqualTo(PrintedProcessId()));
        Assert.That(reported.StartMethod, Is.EqualTo(ProcessEvent.StartMethodValue.Launch));
        Assert.That(reported.IsLocalProcess, Is.True);
        Assert.That(reported.Name, Is.EqualTo(Path.GetFileName(ProgramPath)));
    }

    [Test]
    public void TerminalLaunchReportsTheProcessItStartedTest() {
        // The test stands in for the client's terminal: the adapter asks for another instance of itself to be
        // run in the terminal ('--connection'), and the terminal host runs right here instead of in a terminal window
        Host.RequestReceived += (_, args) => {
            if (args.Args is not RunInTerminalArguments runInTerminal)
                return;
            var connection = runInTerminal.Args.FirstOrDefault(it => it.StartsWith(Program.ConnectionOption, StringComparison.Ordinal));
            Assert.That(connection, Is.Not.Null, "The runInTerminal request names no '--connection' pipe");
            Task.Run(() => TerminalHost.Run(connection!.Substring(Program.ConnectionOption.Length)));
            args.Response = new RunInTerminalResponse();
        };

        Launch(properties: new Dictionary<string, JToken> { ["console"] = "integratedTerminal" });
        ConfigurationDone();

        var reported = WaitForEvent<ProcessEvent>();

        Assert.That(reported.SystemProcessId, Is.GreaterThan(0));
        Assert.That(reported.StartMethod, Is.EqualTo(ProcessEvent.StartMethodValue.Launch));
        Assert.That(reported.IsLocalProcess, Is.True);
        Assert.That(reported.Name, Is.EqualTo(Path.GetFileName(ProgramPath)));
        // A terminal debuggee prints to the terminal, so its process is checked directly rather than through OutputEvents
        Assert.That(() => System.Diagnostics.Process.GetProcessById(reported.SystemProcessId!.Value), Throws.Nothing);
    }

    // Read from the retained events rather than the queue: what the program prints and the event
    // naming it arrive in either order
    private int PrintedProcessId() {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline) {
            var printed = ReceivedEvents
                .OfType<OutputEvent>()
                .Select(it => it.Output.Trim())
                .FirstOrDefault(it => it.StartsWith("pid ", StringComparison.Ordinal));

            if (printed is not null)
                return int.Parse(printed["pid ".Length..], System.Globalization.CultureInfo.InvariantCulture);

            System.Threading.Thread.Sleep(25);
        }

        throw new TimeoutException("The debuggee never printed its process id");
    }
}
