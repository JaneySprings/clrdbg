using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class BreakpointCornerTests : BaseDebugTestFixture {
    public BreakpointCornerTests() : base(nameof(BreakpointCornerTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var numbers = new[] { 1, 2, 3 };
        // a comment line without executable code, marker:comment
        var doubled = numbers.Select(x => x * 2).ToArray(); // marker:lambda
        var total = 0;
        foreach (var value in doubled) {
            total += value; // marker:loop
        }
        Console.WriteLine(total); // marker:end
        """;
    }

    [Test]
    public void BreakpointOnCommentLineSnapsToNextStatementTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:comment"));
        ConfigurationDone();

        var bound = WaitForEvent<BreakpointEvent>(it => it.Breakpoint.Verified);
        Assert.That(bound.Breakpoint.Line, Is.EqualTo(GetMarkerLine("marker:lambda")), "The breakpoint snaps to the next line with code");

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:lambda")));
    }

    // The statement's own sequence point spans the whole lambda text, so the resolver deliberately binds
    // the innermost lambda: the breakpoint stops once per enumerated element
    [Test]
    public void SameLineLambdaBreakpointBindsToTheLambdaTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:lambda"), GetMarkerLine("marker:end"));
        ConfigurationDone();

        for (var i = 0; i < 3; i++) {
            var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
            Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:lambda")));
            Continue(stopped.ThreadId!.Value);
        }
        var endStopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(endStopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:end")));
    }

    [Test]
    public void RemovedBreakpointDoesNotStopTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:loop"), GetMarkerLine("marker:end"));
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:loop")));

        // Replacing the file's breakpoints drops the loop one; the remaining iterations must run through
        SetBreakpoints(GetMarkerLine("marker:end"));
        Continue(stopped.ThreadId!.Value);

        var endStopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(endStopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:end")));
    }
}
