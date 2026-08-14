using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class EvaluateTests : BaseDebugTestFixture {
    public EvaluateTests() : base(nameof(EvaluateTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var count = 42;
        var title = "hello";
        var item = new SampleClass();
        Console.WriteLine($"{count} {title} {item.Number}"); // marker:stop

        public class SampleClass {
            public int Number { get; set; } = 7;
            public int GetDoubledNumber() => Number * 2;
        }
        """;
    }

    private int StopAtMarker() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:stop"));
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        return stopped.ThreadId!.Value;
    }

    [Test]
    public void EvaluateExpressionTest() {
        var threadId = StopAtMarker();
        var result = Evaluate("count + 8", threadId);
        Assert.That(result.Result, Is.EqualTo("50"));
        Assert.That(result.Type, Is.EqualTo("int"));
    }

    [Test]
    public void EvaluateMemberAccessTest() {
        var threadId = StopAtMarker();
        var result = Evaluate("item.Number", threadId);
        Assert.That(result.Result, Is.EqualTo("7"));
    }

    [Test]
    public void EvaluateMethodCallTest() {
        var threadId = StopAtMarker();
        var result = Evaluate("item.GetDoubledNumber()", threadId);
        Assert.That(result.Result, Is.EqualTo("14"));
    }

    [Test]
    public void EvaluateObjectChildrenTest() {
        var threadId = StopAtMarker();
        var result = Evaluate("item", threadId);
        Assert.That(result.VariablesReference, Is.GreaterThan(0), "Objects must be expandable");

        var children = GetVariables(result.VariablesReference);
        Assert.That(children.Any(it => it.Name == "Number [int]" && it.Value == "7"));
    }

    [Test]
    public void EvaluateInvalidExpressionTest() {
        var threadId = StopAtMarker();
        Assert.Throws<ProtocolException>(() => Evaluate("nonExistentVariable", threadId));
    }
}