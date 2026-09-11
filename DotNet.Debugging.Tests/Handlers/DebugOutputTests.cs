using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// What the debuggee logs to the debugger (Debug.WriteLine and friends) reaches the client as console output
public class DebugOutputTests : BaseDebugTestFixture {
    public DebugOutputTests() : base(nameof(DebugOutputTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        using System.Diagnostics;

        Debug.WriteLine("debug says hello");
        Trace.WriteLine("trace says hello");
        Debugger.Log(0, null, "logger says hello");
        Console.WriteLine("done"); // marker:end
        """;
    }

    [Test]
    public void DebugWriteLineTest() {
        Launch();
        ConfigurationDone();

        var debugOutput = WaitForEvent<OutputEvent>(it => it.Output.Contains("debug says hello"));
        Assert.That(debugOutput.Category, Is.EqualTo(OutputEvent.CategoryValue.Console));
        Assert.That(debugOutput.Output, Is.EqualTo("debug says hello" + Environment.NewLine));
    }

    [Test]
    public void TraceAndDebuggerLogTest() {
        Launch();
        ConfigurationDone();

        var traceOutput = WaitForEvent<OutputEvent>(it => it.Output.Contains("trace says hello"));
        Assert.That(traceOutput.Category, Is.EqualTo(OutputEvent.CategoryValue.Console));
        var logOutput = WaitForEvent<OutputEvent>(it => it.Output.Contains("logger says hello"));
        Assert.That(logOutput.Category, Is.EqualTo(OutputEvent.CategoryValue.Console));
        WaitForEvent<TerminatedEvent>();
    }
}
