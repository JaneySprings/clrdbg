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

    private int StopAtFirstLine() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:first"));
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        return stopped.ThreadId!.Value;
    }

    [Test]
    public void NextTest() {
        var threadId = StopAtFirstLine();
        Host.SendRequestSync(new NextRequest() { ThreadId = threadId });

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Step);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:second")));
    }

    [Test]
    public void StepInTest() {
        var threadId = StopAtFirstLine();
        Host.SendRequestSync(new StepInRequest() { ThreadId = threadId });

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Step);
        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Name, Does.Contain("Add"));
    }

    [Test]
    public void StepOutTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:inside"));
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);

        Host.SendRequestSync(new StepOutRequest() { ThreadId = stopped.ThreadId!.Value });

        var stepStopped = WaitForStopped(StoppedEvent.ReasonValue.Step);
        var frame = GetTopStackFrame(stepStopped.ThreadId!.Value);
        Assert.That(frame.Name, Does.Contain("Main"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:first")));
    }
}