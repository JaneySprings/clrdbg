using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A condition that cannot be evaluated stops rather than passing the breakpoint silently: the failure goes to the
// debug output with its location, the stop carries no text
public class ConditionFailureBreakpointTests : BaseDebugTestFixture {
    public ConditionFailureBreakpointTests() : base(nameof(ConditionFailureBreakpointTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var count = 3; // marker:notBoolean
        Console.WriteLine(count); // marker:unresolved
        Console.WriteLine(count); // marker:throws
        Console.WriteLine("end"); // marker:end

        static class Checks {
            public static bool Fail() {
                throw new InvalidOperationException("check failed");
            }
        }
        """;
    }

    [Test]
    public void FailingConditionsStopTest() {
        Launch();
        SetBreakpoints(
            new SourceBreakpoint() { Line = GetMarkerLine("marker:notBoolean"), Condition = "count" },
            new SourceBreakpoint() { Line = GetMarkerLine("marker:unresolved"), Condition = "missingName" },
            new SourceBreakpoint() { Line = GetMarkerLine("marker:throws"), Condition = "Checks.Fail()" },
            new SourceBreakpoint() { Line = GetMarkerLine("marker:end") });
        ConfigurationDone();

        ExpectFailedConditionStop(1, "marker:notBoolean", "The breakpoint condition 'count' could not be evaluated: the result is not a boolean");
        ExpectFailedConditionStop(2, "marker:unresolved", "The breakpoint condition 'missingName' could not be evaluated: error CS0103: The name 'missingName' does not exist in the current context");
        ExpectFailedConditionStop(3, "marker:throws", "The breakpoint condition 'Checks.Fail()' could not be evaluated: System.InvalidOperationException was thrown");

        var end = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(end.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:end")));
        Assert.That(end.Text, Is.Null);
    }

    private void ExpectFailedConditionStop(int breakpointId, string marker, string message) {
        var output = WaitForEvent<OutputEvent>(it => it.Category == OutputEvent.CategoryValue.Console && it.Output.StartsWith("The breakpoint condition"));
        Assert.That(output.Output, Is.EqualTo($"{message} ({ProgramFilePath}:{GetMarkerLine(marker)}){Environment.NewLine}"));

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine(marker)));
        Assert.That(stopped.HitBreakpointIds, Is.EqualTo(new[] { breakpointId }));
        Assert.That(stopped.Text, Is.Null, "The stop carries no text of its own");
        Continue(stopped.ThreadId!.Value);
    }
}
