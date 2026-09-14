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
    public void OutputMessagesTest() {
        Launch();
        var breakpoints = SetBreakpoints(GetMarkerLine("marker:end"));
        Assert.That(breakpoints[0].Message, Is.EqualTo("The breakpoint is not bound yet: no loaded module with symbols contains this location."));
        var exceptionBreakpoints = SetExceptionBreakpoints(Array.Empty<string>(), ("user-unhandled", null), ("no-such-filter", null));
        Assert.That(exceptionBreakpoints.Select(it => it.Verified), Is.EqualTo(new[] { true, false }));
        ConfigurationDone();

        var moduleEvent = WaitForEvent<ModuleEvent>(it => it.Module.IsUserCode == true);
        var moduleId = Convert.ToInt32(moduleEvent.Module.Id, System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(moduleId, Is.GreaterThan(0));
        Assert.That(moduleEvent.Module.IsOptimized, Is.False);
        Assert.That(moduleEvent.Module.Version, Does.Match(@"^\d+\.\d+\.\d+\.\d+$"));
        Assert.That(moduleEvent.Module.SymbolStatus, Is.EqualTo("Symbols loaded."));
        Assert.That(moduleEvent.Module.SymbolFilePath, Does.EndWith(".pdb"));

        WaitForEvent<BreakpointEvent>(it => it.Breakpoint.Verified);

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var stackTrace = Host.SendRequestSync(new StackTraceRequest() { ThreadId = stopped.ThreadId!.Value });
        Assert.That(stackTrace.TotalFrames, Is.EqualTo(stackTrace.StackFrames.Count));
        var frame = stackTrace.StackFrames[0];
        Assert.That(frame.Name, Does.Match(@"^.+\.dll!.+\(string\[\] args\)$"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:end")));
        Assert.That(Convert.ToInt32(frame.ModuleId, System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(moduleId));

        Continue(stopped.ThreadId!.Value);
        var exitMessage = WaitForEvent<OutputEvent>(it => it.Output.Contains("has exited with code"));
        Assert.That(exitMessage.Category, Is.EqualTo(OutputEvent.CategoryValue.Console));
        Assert.That(exitMessage.Output.Trim(), Does.Match(@"^The program '.+' has exited with code 0 \(0x0\)\.$"));
        WaitForEvent<ExitedEvent>();
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