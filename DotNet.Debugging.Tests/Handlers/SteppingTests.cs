using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class SteppingTests : BaseDebugTestFixture {
    public SteppingTests() : base(nameof(SteppingTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var result = Add(1, 2); // marker:first
        result = Add(result, 3); // marker:second
        Console.WriteLine(result); // marker:end

        static int Add(int left, int right) {
            var sum = left + right; // marker:inside
            return sum;
        }
        """;
    }

    [Test]
    public void NextTest() {
        var threadId = LaunchToMarker("marker:first");
        var frame = StepOver(threadId);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:second")));
    }

    [Test]
    public void StepInTest() {
        var threadId = LaunchToMarker("marker:first");
        var frame = StepIn(threadId);
        Assert.That(frame.Name, Does.Contain("Add"));
    }

    [Test]
    public void StepOutTest() {
        var threadId = LaunchToMarker("marker:inside");
        var frame = StepOut(threadId);
        Assert.That(frame.Name, Does.Contain("Main"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:first")));
    }

    // A breakpoint inside the stepped-over call whose hit count does not stop must leave the step alive,
    // pausing and silently resuming the debuggee would otherwise swallow it and the program runs free
    [Test]
    public void StepOverSurvivesANonStoppingBreakpointInsideTheCall() {
        var threadId = LaunchToMarker("marker:first");
        SetBreakpoints(new SourceBreakpoint() { Line = GetMarkerLine("marker:inside"), HitCondition = "5" });
        var frame = StepOver(threadId);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:second")));
    }
}