using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// The program's PDB is moved away from the binary, so symbols only load through the configured search path
public class SymbolSearchPathTests : BaseDebugTestFixture {
    public SymbolSearchPathTests() : base(nameof(SymbolSearchPathTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var value = 1; // marker:first
        Console.WriteLine(value);
        """;
    }

    [Test]
    public void SymbolsAreFoundThroughSearchPathTest() {
        var symbolsDirectory = Path.Combine(SandboxDirectory, "MovedSymbols");
        Directory.CreateDirectory(symbolsDirectory);
        var pdbPath = Path.ChangeExtension(ProgramPath, ".pdb");
        var movedPdbPath = Path.Combine(symbolsDirectory, Path.GetFileName(pdbPath));
        File.Move(pdbPath, movedPdbPath, true);

        Launch(properties: new Dictionary<string, JToken> {
            ["symbolOptions"] = JToken.FromObject(new Dictionary<string, object> { ["searchPaths"] = new[] { symbolsDirectory } }),
        });
        SetBreakpoints(GetMarkerLine("marker:first"));
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:first")));
        Assert.That(frame.Source?.Path, Is.EqualTo(ProgramFilePath));
    }
}
