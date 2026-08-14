using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class BreakpointTests : BaseDebugTestFixture {
    public BreakpointTests() : base(nameof(BreakpointTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var counter = 0;
        for (var i = 0; i < 5; i++) {
            counter += i; // marker:loop
        }
        Console.WriteLine(counter); // marker:end
        """;
    }

    [Test]
    public void BreakpointHitTest() {
        Launch();
        var breakpoints = SetBreakpoints(GetMarkerLine("marker:loop"));
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(stopped.HitBreakpointIds, Is.EquivalentTo(new[] { breakpoints[0].Id!.Value }));

        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:loop")));
        Assert.That(frame.Source?.Path, Is.EqualTo(ProgramFilePath));
    }

    [Test]
    public void BreakpointBindingEventTest() {
        Launch();
        var breakpoints = SetBreakpoints(GetMarkerLine("marker:end"));
        Assert.That(breakpoints[0].Verified, Is.False, "A breakpoint cannot be bound before the debuggee is launched");
        ConfigurationDone();

        var breakpointEvent = WaitForEvent<BreakpointEvent>(it => it.Breakpoint.Verified);
        Assert.That(breakpointEvent.Breakpoint.Id, Is.EqualTo(breakpoints[0].Id));
        Assert.That(breakpointEvent.Breakpoint.Line, Is.EqualTo(GetMarkerLine("marker:end")));
    }

    [Test]
    public void ConditionalBreakpointTest() {
        Launch();
        SetBreakpoints(new SourceBreakpoint() {
            Line = GetMarkerLine("marker:loop"),
            Condition = "i == 3",
        });
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var result = Evaluate("i", stopped.ThreadId!.Value);
        Assert.That(result.Result, Is.EqualTo("3"));
    }

    [Test]
    public void HitConditionBreakpointTest() {
        Launch();
        SetBreakpoints(new SourceBreakpoint() {
            Line = GetMarkerLine("marker:loop"),
            HitCondition = "3",
        });
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var result = Evaluate("i", stopped.ThreadId!.Value);
        Assert.That(result.Result, Is.EqualTo("2"), "The third hit of the breakpoint happens at 'i == 2'");
    }

    [Test]
    public void LogpointTest() {
        Launch();
        SetBreakpoints(
            new SourceBreakpoint() { Line = GetMarkerLine("marker:loop"), LogMessage = "counter is {counter}, i is {i}" },
            new SourceBreakpoint() { Line = GetMarkerLine("marker:end") });
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:end")), "Logpoints must not stop the execution");

        var logpointOutputs = ReceivedEvents.OfType<OutputEvent>().Where(it => it.Output.StartsWith("[LogPoint]")).ToList();
        Assert.That(logpointOutputs, Has.Count.EqualTo(5));
        Assert.That(logpointOutputs[0].Output.Trim(), Is.EqualTo("[LogPoint]: counter is 0, i is 0"));
        Assert.That(logpointOutputs[4].Output.Trim(), Is.EqualTo("[LogPoint]: counter is 6, i is 4"));
    }
}
