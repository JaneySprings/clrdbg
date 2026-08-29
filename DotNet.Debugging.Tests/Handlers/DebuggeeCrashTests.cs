using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// The debuggee dies in the middle of a func eval (the way a crashing property getter kills it): the
// request must fail and the session must report the exit instead of hanging on the engine lock forever
public class DebuggeeCrashTests : BaseDebugTestFixture {
    public DebuggeeCrashTests() : base(nameof(DebuggeeCrashTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var value = 1; // marker:stop
        Console.WriteLine(value);
        """;
    }

    [Test]
    public void EvalKillingTheDebuggeeTerminatesTheSessionTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:stop"));
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var threadId = stopped.ThreadId!.Value;

        // The evaluation kills the debuggee; the request must come back (as an error), not hang
        var evaluation = Task.Run(() => {
            try {
                Evaluate("System.Environment.FailFast(\"the debuggee dies inside the evaluation\")", threadId);
            }
            catch (Exception) {
                // An error response is the expected outcome
            }
        });
        Assert.That(evaluation.Wait(TimeSpan.FromSeconds(30)), Is.True, "The evaluate request never completed");
        Assert.That(WaitForEvent<TerminatedEvent>(), Is.Not.Null);
    }
}
