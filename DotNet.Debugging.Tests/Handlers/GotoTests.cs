using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class GotoTests : BaseDebugTestFixture {
    public GotoTests() : base(nameof(GotoTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var value = 0;
        value = 1; // marker:start
        value = 2;
        value = 3; // marker:target
        Console.WriteLine(value); // marker:end
        """;
    }

    [Test]
    public void GotoTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:start"), GetMarkerLine("marker:end"));
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var threadId = stopped.ThreadId!.Value;

        var targets = Host.SendRequestSync(new GotoTargetsRequest() {
            Source = new Source() { Path = ProgramFilePath },
            Line = GetMarkerLine("marker:target"),
        });
        Assert.That(targets.Targets, Has.Count.EqualTo(1));
        Assert.That(targets.Targets[0].Label, Is.EqualTo("Jump to cursor"));

        Host.SendRequestSync(new GotoRequest() { ThreadId = threadId, TargetId = targets.Targets[0].Id });
        var gotoStopped = WaitForStopped(StoppedEvent.ReasonValue.Goto);
        Assert.That(GetTopStackFrame(gotoStopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:target")));

        // The assignments before the target line were skipped
        Continue(threadId);
        var endStopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(Evaluate("value", endStopped.ThreadId!.Value).Result, Is.EqualTo("3"));
    }

    [Test]
    public void GotoOutsideOfMethodTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:start"));
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);

        var targets = Host.SendRequestSync(new GotoTargetsRequest() {
            Source = new Source() { Path = ProgramFilePath },
            Line = 1000,
        });
        Assert.That(
            () => Host.SendRequestSync(new GotoRequest() { ThreadId = stopped.ThreadId!.Value, TargetId = targets.Targets[0].Id }),
            Throws.Exception,
            "Jumping to a location without executable code must fail");
    }
}