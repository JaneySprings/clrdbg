using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// The program is built in the sandbox but the "client" only knows a copy of the source in another
// directory; 'sourceFileMap' maps the compile-time directory to it
public class SourceFileMapTests : BaseDebugTestFixture {
    public SourceFileMapTests() : base(nameof(SourceFileMapTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var value = 1; // marker:first
        value = value + 1; // marker:second
        Console.WriteLine(value);
        """;
    }

    [Test]
    public void BreakpointAndFramePathsAreMappedTest() {
        var movedDirectory = Path.Combine(SandboxDirectory, "SourceFileMapMoved");
        Directory.CreateDirectory(movedDirectory);
        var movedPath = Path.Combine(movedDirectory, "Program.cs");
        File.Copy(ProgramFilePath, movedPath, true);

        Launch(properties: new Dictionary<string, JToken> {
            ["sourceFileMap"] = JToken.FromObject(new Dictionary<string, string> { [ProjectDirectory] = movedDirectory }),
        });
        Host.SendRequestSync(new SetBreakpointsRequest() {
            Source = new Source() { Path = movedPath },
            Breakpoints = new List<SourceBreakpoint> { new SourceBreakpoint() { Line = GetMarkerLine("marker:first") } },
        });
        ConfigurationDone();

        // The breakpoint binds through the compile-time path and is reported back with the mapped one
        var bound = WaitForEvent<BreakpointEvent>(it => it.Breakpoint.Verified);
        Assert.That(bound.Breakpoint.Source?.Path, Is.EqualTo(movedPath));

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:first")));
        Assert.That(frame.Source?.Path, Is.EqualTo(movedPath));
    }

    // A map entry for a build server that did not build this module (the PDB holds the local paths) must not get
    // in the way: the client's path matches the documents directly
    [Test]
    public void UnrelatedMapEntryKeepsLocalPathsBindingTest() {
        Launch(properties: new Dictionary<string, JToken> {
            ["sourceFileMap"] = JToken.FromObject(new Dictionary<string, string> { ["D:\\BuildServer\\Src"] = ProjectDirectory }),
        });
        Host.SendRequestSync(new SetBreakpointsRequest() {
            Source = new Source() { Path = ProgramFilePath },
            Breakpoints = new List<SourceBreakpoint> { new SourceBreakpoint() { Line = GetMarkerLine("marker:first") } },
        });
        ConfigurationDone();

        var bound = WaitForEvent<BreakpointEvent>(it => it.Breakpoint.Verified);
        Assert.That(bound.Breakpoint.Source?.Path, Is.EqualTo(ProgramFilePath));

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:first")));
        Assert.That(frame.Source?.Path, Is.EqualTo(ProgramFilePath));
    }
}
