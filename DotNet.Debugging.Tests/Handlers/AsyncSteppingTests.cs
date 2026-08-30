using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Steps across 'await' points are carried by breakpoints on the yield and resume offsets rather than by a
// plain stepper, which would run to the caller when the method yields (Stepping/AsyncStepper)
public class AsyncSteppingTests : BaseDebugTestFixture {
    public AsyncSteppingTests() : base(nameof(AsyncSteppingTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var value = await ComputeAsync(); // marker:first
        var doubled = value * 2; // marker:afterFirst
        await Task.Delay(10); // marker:delay
        Console.WriteLine(doubled); // marker:end

        static async Task<int> ComputeAsync() { // marker:computeHeader
            await Task.Delay(50); // marker:insideCompute
            return 21; // marker:return
        } // marker:computeEnd
        """;
    }

    // The resume of 'await Task.Delay' happens on a thread pool thread, the step must follow it there
    [Test]
    public void StepOverAwaitTest() {
        var threadId = LaunchToMarker("marker:delay");
        var frame = StepOver(threadId);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:end")));
    }

    // Stepping over the whole awaited call must not land inside the callee or run away to the end
    [Test]
    public void StepOverAwaitedCallTest() {
        var threadId = LaunchToMarker("marker:first");
        var frame = StepOver(threadId);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:afterFirst")));
    }

    [Test]
    public void StepIntoAsyncMethodTest() {
        var threadId = LaunchToMarker("marker:first");
        var frame = StepIn(threadId);
        Assert.That(frame.Name, Does.Contain("ComputeAsync"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:computeHeader")));
    }

    // Stepping out of a resumed async method waits for its task: the step lands where the caller awaits
    [Test]
    public void StepOutOfAsyncMethodTest() {
        var threadId = LaunchToMarker("marker:return");
        var frame = StepOut(threadId);
        Assert.That(frame.Name, Does.Contain("Main"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:first")));
    }

    // Past the last statement of an async method the only way forward is out of it
    [Test]
    public void StepOverLastStatementLeavesTheAsyncMethodTest() {
        var threadId = LaunchToMarker("marker:return");
        var frame = StepOver(threadId);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:computeEnd")), "The step lands on the closing brace first");

        var mainFrame = StepOver(threadId);
        Assert.That(mainFrame.Name, Does.Contain("Main"));
        Assert.That(mainFrame.Line, Is.EqualTo(GetMarkerLine("marker:first")), "The caller resumes at its await");
    }
}
