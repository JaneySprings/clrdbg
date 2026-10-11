using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A file edited after the build no longer has the content the PDB records: a breakpoint set in it is refused with
// 'requireExactSource' (the default) and bound anyway with it off
public class EditedSourceTests : BaseDebugTestFixture {
    public EditedSourceTests() : base(nameof(EditedSourceTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var value = 1; // marker:first
        value = value + 1; // marker:second
        Console.WriteLine(value); // marker:third
        """;
    }

    [Test]
    public void EditedFileIsRefusedTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:first"));
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);

        var breakpoints = WithEditedProgram(() => SetBreakpoints(GetMarkerLine("marker:second"), GetMarkerLine("marker:third")));
        Assert.That(breakpoints.Select(it => it.Verified), Is.All.False);
        Assert.That(breakpoints.Select(it => it.Message), Is.All.EqualTo(Adapter.Resources.MsgBreakpointSourceMismatch));

        Continue(stopped.ThreadId!.Value);
        WaitForEvent<TerminatedEvent>();
    }

    [Test]
    public void EditedFileBindsWithoutExactSourceTest() {
        Launch(properties: new Dictionary<string, JToken> { ["requireExactSource"] = false });
        SetBreakpoints(GetMarkerLine("marker:first"));
        ConfigurationDone();
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);

        var breakpoints = WithEditedProgram(() => SetBreakpoints(GetMarkerLine("marker:second")));
        Assert.That(breakpoints[0].Verified, Is.True);

        Continue(stopped.ThreadId!.Value);
        stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:second")));
    }

    // Appended at the end, so the breakpoint lines stay where they are
    private T WithEditedProgram<T>(Func<T> action) {
        var original = File.ReadAllText(ProgramFilePath);
        try {
            File.AppendAllText(ProgramFilePath, "// edited after the build" + Environment.NewLine);
            return action();
        }
        finally {
            File.WriteAllText(ProgramFilePath, original);
        }
    }
}
