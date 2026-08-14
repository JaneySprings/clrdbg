using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class ThreadTests : BaseDebugTestFixture {
    public ThreadTests() : base(nameof(ThreadTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var worker = new Thread(() => Thread.Sleep(60000)) { Name = "My Worker", IsBackground = true };
        worker.Start();
        _ = Task.Run(() => Thread.Sleep(60000));
        Thread.Sleep(500);
        Console.WriteLine("ready"); // marker:stop
        """;
    }

    [Test]
    public void ThreadNamesTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:stop"));
        ConfigurationDone();
        WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);

        var threads = Host.SendRequestSync(new ThreadsRequest()).Threads;
        var names = threads.Select(it => it.Name).ToList();

        Assert.That(names, Does.Contain("Main Thread"));
        Assert.That(names, Does.Contain("My Worker"), "The managed thread name must be displayed");
        Assert.That(names.Any(it => it.StartsWith("Thread ")), Is.False, "Unnamed threads must be displayed as '<No Name>'");
    }

    [Test]
    public void StopAtEntryTest() {
        Launch(stopAtEntry: true);
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Entry);
        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Line, Is.EqualTo(1), "The entry stop must happen on the first line of the program");
        Assert.That(frame.Name, Does.Contain("Main"));
    }
}