using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Several breakpoints resolving to one statement stop there once, naming all of them; a breakpoint given by path
// binds to that document even when another folder of the project holds an equally named file
public class SameLocationBreakpointTests : BaseDebugTestFixture {
    public SameLocationBreakpointTests() : base(nameof(SameLocationBreakpointTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var total = Adder.Plus(1); // marker:call
        Console.WriteLine(total + Shared.Totals.Value()); // marker:end

        public static class Adder {
            public static int Plus(int input) { // marker:entry
                return input + 1;
            }
        }
        """;
    }
    // A same-named file whose lines all carry code, so a breakpoint matched by file name alone would bind into it
    protected override void CreateProjectFiles(string projectDirectory) {
        var sharedDirectory = Path.Combine(projectDirectory, "Shared");
        Directory.CreateDirectory(sharedDirectory);
        File.WriteAllText(Path.Combine(sharedDirectory, "Program.cs"), """
        namespace Shared;
        public static class Totals {
            public static int Value() {
                var first = 10;
                var second = first + 10;
                var third = second + 10;
                var fourth = third + 10;
                var fifth = fourth + 10;
                return fifth;
            }
        }
        """);
    }

    [Test]
    public void TwoBreakpointsOnOneLineStopOnceTest() {
        Launch();
        var line = GetMarkerLine("marker:call");
        SetBreakpoints(new SourceBreakpoint() { Line = line }, new SourceBreakpoint() { Line = line, Column = 5 }, new SourceBreakpoint() { Line = GetMarkerLine("marker:end") });
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(line));
        Assert.That(stopped.HitBreakpointIds, Is.EqualTo(new[] { 1, 2 }), "Both breakpoints are reported for the one stop");
        Continue(stopped.ThreadId!.Value);

        var next = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(next.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:end")), "The statement is stopped at once, however many breakpoints resolve to it");
    }

    // A function breakpoint binds at the method's first sequence point, where a source breakpoint on its header line binds too
    [Test]
    public void SourceAndFunctionBreakpointOnOneStatementStopOnceTest() {
        Launch();
        var entry = GetMarkerLine("marker:entry");
        SetBreakpoints(entry, GetMarkerLine("marker:end"));
        var functionBreakpoint = Host.SendRequestSync(new SetFunctionBreakpointsRequest() { Breakpoints = [new FunctionBreakpoint() { Name = "Adder.Plus" }] }).Breakpoints[0];
        ConfigurationDone();

        // A function breakpoint is reported as not found until its module loads, then with the location it bound to
        var notFound = WaitForEvent<BreakpointEvent>(it => it.Breakpoint.Id == functionBreakpoint.Id && !it.Breakpoint.Verified && it.Breakpoint.Message != "The breakpoint is pending and will be resolved when debugging starts.");
        Assert.That(notFound.Breakpoint.Message, Is.EqualTo("The function cannot be found: Adder.Plus"));
        var bound = WaitForEvent<BreakpointEvent>(it => it.Breakpoint.Id == functionBreakpoint.Id && it.Breakpoint.Verified);
        Assert.That(bound.Breakpoint.Line, Is.EqualTo(entry));
        Assert.That(bound.Breakpoint.Source?.Path, Is.EqualTo(ProgramFilePath));

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(entry));
        Assert.That(stopped.HitBreakpointIds, Has.Count.EqualTo(2), "Both the source and the function breakpoint are reported for the stop");
        Continue(stopped.ThreadId!.Value);

        var next = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(next.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:end")));
    }

    [Test]
    public void BreakpointBindsToTheDocumentAtItsPathTest() {
        Launch();
        var line = GetMarkerLine("marker:call");
        SetBreakpoints(line);
        ConfigurationDone();

        var bound = WaitForEvent<BreakpointEvent>(it => it.Breakpoint.Verified);
        Assert.That(bound.Breakpoint.Source?.Path, Is.EqualTo(ProgramFilePath));
        Assert.That(bound.Breakpoint.Line, Is.EqualTo(line));

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Source?.Path, Is.EqualTo(ProgramFilePath), "The equally named file in the other folder must not win");
        Assert.That(frame.Line, Is.EqualTo(line));
    }
}
