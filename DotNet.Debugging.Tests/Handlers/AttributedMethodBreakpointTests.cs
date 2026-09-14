using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// The debugger attributes steer stepping and exception stops, not the breakpoints the user sets: one in a
// [DebuggerStepThrough], [DebuggerNonUserCode] or [DebuggerHidden] method binds and is hit in every mode
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
    public void AttributedMethodsTakeBreakpointsUnderJustMyCodeTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:stepThrough"), GetMarkerLine("marker:nonUserCode"), GetMarkerLine("marker:hidden"), GetMarkerLine("marker:end"));
        ConfigurationDone();

        var stops = CollectStopsUntilExit();
        Assert.That(stops.Select(it => it.HitBreakpointIds![0]), Is.EqualTo(new[] { 1, 2, 3, 4 }), "Every breakpoint is hit");
    }

    [Test]
    public void AttributedMethodsTakeBreakpointsWithoutJustMyCodeTest() {
        Launch(justMyCode: false);
        SetBreakpoints(GetMarkerLine("marker:stepThrough"), GetMarkerLine("marker:nonUserCode"), GetMarkerLine("marker:hidden"), GetMarkerLine("marker:end"));
        ConfigurationDone();

        var stops = CollectStopsUntilExit();
        Assert.That(stops.Select(it => it.HitBreakpointIds![0]), Is.EqualTo(new[] { 1, 2, 3, 4 }), "Every breakpoint is hit");
    }
}
