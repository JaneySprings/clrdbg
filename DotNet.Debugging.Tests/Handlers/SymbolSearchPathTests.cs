using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// The program's PDB is moved away from the binary, so symbols only load through the configured search path
public class SymbolSearchPathTests : BaseDebugTestFixture {
    public SymbolSearchPathTests() : base(nameof(SymbolSearchPathTests)) { }

    // The test moves the PDB out of the build output, a reused sandbox would be left without one
    protected override bool ReuseSandbox => false;

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

        var threadId = LaunchToMarker("marker:first", properties: new Dictionary<string, JToken> {
            ["symbolOptions"] = JToken.FromObject(new Dictionary<string, object> { ["searchPaths"] = new[] { symbolsDirectory } }),
        });
        var frame = GetTopStackFrame(threadId);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:first")));
        Assert.That(frame.Source?.Path, Is.EqualTo(ProgramFilePath));
    }
}
