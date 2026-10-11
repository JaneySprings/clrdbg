using System.Runtime.CompilerServices;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Metadata;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Resolves breakpoints against the test assembly's own PDB, where this file is a known document
public class DocumentMatchingTests {
    private static readonly SourceFileMapper noMap = new SourceFileMapper(new Dictionary<string, string>());

    private ModuleMetadataReader reader = null!;
    private string tempDirectory = null!;

    [OneTimeSetUp]
    public void SetUp() {
        var loaded = ModuleMetadataReader.TryLoad(typeof(DocumentMatchingTests).Assembly.Location);
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.HasSymbols, Is.True);
        reader = loaded;
        tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDirectory);
    }
    [OneTimeTearDown]
    public void TearDown() {
        reader.Dispose();
        Directory.Delete(tempDirectory, recursive: true);
    }

    [Test]
    public void ExactPathMatchTest() {
        var resolved = reader.ResolveBreakpoint(GetThisFilePath(), 1, null, noMap, requireExactSource: true, out var status);
        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.Location.FilePath, Is.EqualTo(GetThisFilePath()));
        Assert.That(resolved.MatchKind, Is.EqualTo(DocumentMatchKind.Path));
        Assert.That(status, Is.EqualTo(BreakpointStatus.Bound));
    }

    [Test]
    public void MappedDocumentPathMatchesExactlyTest() {
        // The PDB was built from this directory, the client has the same sources in another one: the map
        // translates the document to the client's path
        var clientPath = CreateClientCopy("client");
        var map = new SourceFileMapper(new Dictionary<string, string> { [Path.GetDirectoryName(GetThisFilePath())!] = Path.GetDirectoryName(clientPath)! });

        var resolved = reader.ResolveBreakpoint(clientPath, 1, null, map, requireExactSource: true, out var status);
        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.Location.FilePath, Is.EqualTo(GetThisFilePath()));
        Assert.That(resolved.MatchKind, Is.EqualTo(DocumentMatchKind.Path));
        Assert.That(status, Is.EqualTo(BreakpointStatus.Bound));
    }

    [Test]
    public void UnrelatedMapEntryKeepsExactPathMatchTest() {
        // A map entry for a build server that did not build this module leaves its documents alone
        var map = new SourceFileMapper(new Dictionary<string, string> { ["D:\\BuildServer\\Src"] = Path.GetDirectoryName(GetThisFilePath())! });

        var resolved = reader.ResolveBreakpoint(GetThisFilePath(), 1, null, map, requireExactSource: true, out var status);
        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.Location.FilePath, Is.EqualTo(GetThisFilePath()));
        Assert.That(resolved.MatchKind, Is.EqualTo(DocumentMatchKind.Path));
        Assert.That(status, Is.EqualTo(BreakpointStatus.Bound));
    }

    [Test]
    public void EditedFileIsRejectedUnlessExactSourceIsOffTest() {
        // The client's copy of the document was edited after the build: its content no longer hashes like the PDB says
        var clientPath = CreateClientCopy("edited");
        File.AppendAllText(clientPath, "// edited after the build" + Environment.NewLine);
        var map = new SourceFileMapper(new Dictionary<string, string> { [Path.GetDirectoryName(GetThisFilePath())!] = Path.GetDirectoryName(clientPath)! });

        Assert.That(reader.ResolveBreakpoint(clientPath, 1, null, map, requireExactSource: true, out var status), Is.Null);
        Assert.That(status, Is.EqualTo(BreakpointStatus.SourceMismatch));
        var resolved = reader.ResolveBreakpoint(clientPath, 1, null, map, requireExactSource: false, out status);
        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.MatchKind, Is.EqualTo(DocumentMatchKind.Path));
        Assert.That(status, Is.EqualTo(BreakpointStatus.Bound));
    }

    [Test]
    public void FileNameMatchRequiresChecksumTest() {
        // Same file name, different location and content
        var strangerPath = Path.Combine(tempDirectory, Path.GetFileName(GetThisFilePath()));
        File.WriteAllText(strangerPath, "class Stranger { void Method() { } }");

        Assert.That(reader.ResolveBreakpoint(strangerPath, 1, null, noMap, requireExactSource: true, out var status), Is.Null);
        Assert.That(status, Is.EqualTo(BreakpointStatus.SourceMismatch));
        var resolved = reader.ResolveBreakpoint(strangerPath, 1, null, noMap, requireExactSource: false, out status);
        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.Location.FilePath, Is.EqualTo(GetThisFilePath()));
        Assert.That(resolved.MatchKind, Is.EqualTo(DocumentMatchKind.Name));
        Assert.That(status, Is.EqualTo(BreakpointStatus.Bound));
    }

    [Test]
    public void FileNameMatchWithMatchingChecksumTest() {
        // The same content in a different location is what the file name fallback exists for
        var copyPath = CreateClientCopy("copy");

        var resolved = reader.ResolveBreakpoint(copyPath, 1, null, noMap, requireExactSource: true, out var status);
        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.Location.FilePath, Is.EqualTo(GetThisFilePath()));
        Assert.That(resolved.MatchKind, Is.EqualTo(DocumentMatchKind.Name));
        Assert.That(status, Is.EqualTo(BreakpointStatus.Bound));
    }

    [Test]
    public void UnknownDocumentTest() {
        // A file the PDB has no equally named document for is not a mismatch
        var unknownPath = Path.Combine(tempDirectory, "Unknown.cs");
        File.WriteAllText(unknownPath, "class Unknown { void Method() { } }");

        Assert.That(reader.ResolveBreakpoint(unknownPath, 1, null, noMap, requireExactSource: true, out var status), Is.Null);
        Assert.That(status, Is.EqualTo(BreakpointStatus.Unbound));
    }

    [Test]
    public void LineWithoutCodeTest() {
        // The document is there, nothing executable is at or after the line
        Assert.That(reader.ResolveBreakpoint(GetThisFilePath(), 100_000, null, noMap, requireExactSource: true, out var status), Is.Null);
        Assert.That(status, Is.EqualTo(BreakpointStatus.NoCode));
    }

    private string CreateClientCopy(string directoryName) {
        var directory = Path.Combine(tempDirectory, directoryName);
        Directory.CreateDirectory(directory);
        var copyPath = Path.Combine(directory, Path.GetFileName(GetThisFilePath()));
        File.Copy(GetThisFilePath(), copyPath, overwrite: true);
        return copyPath;
    }
    private static string GetThisFilePath([CallerFilePath] string filePath = "") {
        return filePath;
    }
}
