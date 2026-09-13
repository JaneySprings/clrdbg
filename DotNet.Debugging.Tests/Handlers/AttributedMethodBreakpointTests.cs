using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A breakpoint in a [DebuggerHidden] method is refused, one in a [DebuggerStepThrough] method is refused under Just My
// Code, one in a [DebuggerNonUserCode] method binds and is hit - the way Microsoft's debugger has it
public class AttributedMethodBreakpointTests : BaseDebugTestFixture {
    public AttributedMethodBreakpointTests() : base(nameof(AttributedMethodBreakpointTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        Skipped();
        Library();
        Concealed();
        Console.WriteLine("end"); // marker:end

        [System.Diagnostics.DebuggerStepThrough]
        static void Skipped() {
            Console.WriteLine("skipped"); // marker:stepThrough
        }
        [System.Diagnostics.DebuggerNonUserCode]
        static void Library() {
            Console.WriteLine("library"); // marker:nonUserCode
        }
        [System.Diagnostics.DebuggerHidden]
        static void Concealed() {
            Console.WriteLine("concealed"); // marker:hidden
        }
        """;
    }

    [Test]
    public void HiddenAndStepThroughMethodsRefuseBreakpointsUnderJustMyCodeTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:stepThrough"), GetMarkerLine("marker:nonUserCode"), GetMarkerLine("marker:hidden"), GetMarkerLine("marker:end"));
        ConfigurationDone();

        var stepThrough = WaitForEvent<BreakpointEvent>(it => it.Breakpoint.Id == 1 && it.Breakpoint.Message != null && it.Breakpoint.Message.StartsWith("Breakpoints cannot"));
        Assert.That(stepThrough.Breakpoint.Verified, Is.False);
        Assert.That(stepThrough.Breakpoint.Message, Is.EqualTo("Breakpoints cannot be set in method or classes with the 'DebuggerStepThrough' attribute when the debugger option 'Just My Code' is enabled."));
        Assert.That(stepThrough.Breakpoint.Line, Is.EqualTo(GetMarkerLine("marker:stepThrough")));
        var hidden = WaitForEvent<BreakpointEvent>(it => it.Breakpoint.Id == 3 && it.Breakpoint.Message != null && it.Breakpoint.Message.StartsWith("Breakpoints cannot"));
        Assert.That(hidden.Breakpoint.Verified, Is.False);
        Assert.That(hidden.Breakpoint.Message, Is.EqualTo("Breakpoints cannot be set in method or class with the 'DebuggerHidden' attribute."));

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(stopped.HitBreakpointIds, Is.EqualTo(new[] { 2 }), "Only the [DebuggerNonUserCode] breakpoint is hit");
        Continue(stopped.ThreadId!.Value);
        var end = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(end.HitBreakpointIds, Is.EqualTo(new[] { 4 }));
    }

    [Test]
    public void StepThroughMethodTakesBreakpointsWithoutJustMyCodeTest() {
        Launch(justMyCode: false);
        SetBreakpoints(GetMarkerLine("marker:stepThrough"), GetMarkerLine("marker:nonUserCode"), GetMarkerLine("marker:hidden"), GetMarkerLine("marker:end"));
        ConfigurationDone();

        var hidden = WaitForEvent<BreakpointEvent>(it => it.Breakpoint.Id == 3 && it.Breakpoint.Message != null && it.Breakpoint.Message.StartsWith("Breakpoints cannot"));
        Assert.That(hidden.Breakpoint.Message, Is.EqualTo("Breakpoints cannot be set in method or class with the 'DebuggerHidden' attribute."), "[DebuggerHidden] refuses breakpoints in every mode");

        var stops = CollectStopsUntilExit();
        Assert.That(stops.Select(it => it.HitBreakpointIds![0]), Is.EqualTo(new[] { 1, 2, 4 }), "[DebuggerStepThrough] and [DebuggerNonUserCode] breakpoints are hit, the hidden one is not");
    }
}
