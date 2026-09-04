using System.Diagnostics;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A getter that never returns must not hold the session: the evaluation is aborted and reported as timed out.
// A getter that throws is a failed read, not the thrown exception presented as the property's value
public class FuncEvalTimeoutTests : BaseDebugTestFixture {
    public FuncEvalTimeoutTests() : base(nameof(FuncEvalTimeoutTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var slow = new SlowClass();
        var thrower = new ThrowingClass();
        Console.WriteLine($"{slow.Fast} {thrower.Good}"); // marker:stop
        Console.WriteLine("done");

        public class SlowClass {
            public int Fast => 1;
            // Never returns on its own, only the debugger's abort ends the evaluation
            public int Blocking {
                get {
                    while (true) {
                        Thread.Sleep(50);
                    }
                }
            }
        }
        public class ThrowingClass {
            public int Good => 5;
            public int Bad => throw new InvalidOperationException("bad");
        }
        """;
    }

    [Test]
    public void BlockingGetterIsAbortedTest() {
        var threadId = LaunchToMarker();
        var slow = GetLocalVariables(threadId).First(it => it.Name.StartsWith("slow"));
        var stopwatch = Stopwatch.StartNew();
        var members = GetVariables(slow.VariablesReference);
        stopwatch.Stop();

        var blocking = members.First(it => it.Name.StartsWith("Blocking"));
        Assert.That(blocking.Value, Is.EqualTo("Evaluation timed out"));
        Assert.That(blocking.PresentationHint?.Attributes, Is.EqualTo(VariablePresentationHint.AttributesValue.FailedEvaluation));
        Assert.That(members.First(it => it.Name.StartsWith("Fast")).Value, Is.EqualTo("1"), "The listing goes on after the aborted getter");
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)), "The abort bounds the wait");
        Assert.That(Evaluate("slow.Fast + 1", threadId).Result, Is.EqualTo("2"), "The session answers requests after the abort");
    }

    [Test]
    public void BlockingEvaluationIsAbortedTest() {
        var threadId = LaunchToMarker();
        var exception = Assert.Throws<ProtocolException>(() => Evaluate("slow.Blocking", threadId));
        Assert.That(exception!.Message, Does.Contain("timed out"));
        Assert.That(Evaluate("slow.Fast", threadId).Result, Is.EqualTo("1"), "The session answers requests after the abort");
    }

    [Test]
    public void ThrowingGetterIsFailedReadTest() {
        var threadId = LaunchToMarker();
        var thrower = GetLocalVariables(threadId).First(it => it.Name.StartsWith("thrower"));
        var members = GetVariables(thrower.VariablesReference);

        var bad = members.First(it => it.Name.StartsWith("Bad"));
        Assert.That(bad.Value, Is.EqualTo("'Bad' threw an exception of type 'System.InvalidOperationException'"));
        Assert.That(bad.VariablesReference, Is.EqualTo(0), "A failed read has nothing to expand");
        Assert.That(bad.PresentationHint?.Attributes, Is.EqualTo(VariablePresentationHint.AttributesValue.FailedEvaluation));
        Assert.That(members.First(it => it.Name.StartsWith("Good")).Value, Is.EqualTo("5"));
    }
}
