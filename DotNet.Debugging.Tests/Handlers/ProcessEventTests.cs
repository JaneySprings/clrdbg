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

    /// <summary>
    /// A launched program's pid reaches the client through the DAP 'process' event and nowhere else,
    /// which leaves a client that launches nothing to show, to log, or to kill if the session goes
    /// wrong. The number has to name the process actually being debugged, and the only witness to that
    /// is the program itself, so it prints its own and the two are compared.
    /// </summary>
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

    /// <summary>
    /// A skipDebug run has no debugger in it, so nothing else could report the pid: there is no engine
    /// event to raise and no attach for the client to have named the process in. The program still runs
    /// and still prints, so the same comparison holds.
    /// </summary>
    [Test]
    public void SkipDebugLaunchReportsTheProcessItStartedTest() {
        Launch(skipDebug: true);
        ConfigurationDone();

        var reported = WaitForEvent<ProcessEvent>();

        Assert.That(reported.SystemProcessId, Is.EqualTo(PrintedProcessId()));
        Assert.That(reported.StartMethod, Is.EqualTo(ProcessEvent.StartMethodValue.Launch));
        Assert.That(reported.Name, Is.EqualTo(ProgramPath));
    }

    /// <summary>
    /// Read from the retained events rather than the queue: what the program prints and the event
    /// naming it arrive in either order, and taking one off the queue would discard the other.
    /// </summary>
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
