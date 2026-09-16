using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// An evaluation is refused on a thread that is not at a safe point (see 'NativeFrameEvaluationTests'). The stops the
// debugger produces itself are safe points and must keep evaluating: the entry point stop and a Debugger.Break()
public class SafePointEvaluationTests : BaseDebugTestFixture {
    public SafePointEvaluationTests() : base(nameof(SafePointEvaluationTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        using System.Diagnostics;

        var value = 41;
        Console.WriteLine("before break");
        Debugger.Break();
        value++;
        Console.WriteLine($"after break {value}");
        """;
    }

    [Test]
    public void EntryStopEvaluatesTest() {
        Launch(stopAtEntry: true);
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Entry);

        Assert.That(Evaluate("System.Environment.TickCount", stopped.ThreadId!.Value).Result, Is.Not.Empty);
    }

    [Test]
    public void DebuggerBreakStopEvaluatesTest() {
        Launch();
        ConfigurationDone();
        var stopped = WaitForStopped();

        Assert.Multiple(() => {
            Assert.That(Evaluate("System.Environment.TickCount", stopped.ThreadId!.Value).Result, Is.Not.Empty);
            Assert.That(Evaluate("value + 1", stopped.ThreadId!.Value).Result, Is.EqualTo("42"));
        });
    }
}
