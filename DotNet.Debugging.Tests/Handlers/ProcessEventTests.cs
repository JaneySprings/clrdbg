using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
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

        // The program the request named, rather than the muxer that ran it
        Assert.That(reported.Name, Is.EqualTo(ProgramPath));
    }

    [Test]
    public void SkipDebugLaunchReportsTheProcessItStartedTest() {
        Launch(skipDebug: true);
        ConfigurationDone();

        var reported = WaitForEvent<ProcessEvent>();

        Assert.That(reported.SystemProcessId, Is.EqualTo(PrintedProcessId()));
        Assert.That(reported.StartMethod, Is.EqualTo(ProcessEvent.StartMethodValue.Launch));
        Assert.That(reported.Name, Is.EqualTo(ProgramPath));
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
