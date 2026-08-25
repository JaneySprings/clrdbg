using System.Runtime.CompilerServices;
using DotNet.Debugging.Engine.Metadata;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Resolves breakpoints against the test assembly's own PDB, where this file is a known document
public class DocumentMatchingTests {
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
        var resolved = reader.ResolveBreakpoint(GetThisFilePath(), 1, null, requireExactSource: true, out var sourceMismatch);
        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.Location.FilePath, Is.EqualTo(GetThisFilePath()));
        Assert.That(resolved.IsExactMatch, Is.True);
        Assert.That(sourceMismatch, Is.False);
    }

    [Test]
    public void FileNameMatchRequiresChecksumTest() {
        // Same file name, different location and content
        var strangerPath = Path.Combine(tempDirectory, Path.GetFileName(GetThisFilePath()));
        File.WriteAllText(strangerPath, "class Stranger { void Method() { } }");

        Assert.That(reader.ResolveBreakpoint(strangerPath, 1, null, requireExactSource: true, out var sourceMismatch), Is.Null);
        Assert.That(sourceMismatch, Is.True);
        var resolved = reader.ResolveBreakpoint(strangerPath, 1, null, requireExactSource: false, out sourceMismatch);
        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.Location.FilePath, Is.EqualTo(GetThisFilePath()));
        // A binding through an unverified name match is loose, so a better module can supersede it
        Assert.That(resolved.IsExactMatch, Is.False);
        Assert.That(sourceMismatch, Is.False);
    }

    [Test]
    public void FileNameMatchWithMatchingChecksumTest() {
        // The same content in a different location is what the file name fallback exists for
        var copyDirectory = Path.Combine(tempDirectory, "copy");
        Directory.CreateDirectory(copyDirectory);
        var copyPath = Path.Combine(copyDirectory, Path.GetFileName(GetThisFilePath()));
        File.Copy(GetThisFilePath(), copyPath, overwrite: true);

        var resolved = reader.ResolveBreakpoint(copyPath, 1, null, requireExactSource: true, out var sourceMismatch);
        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.Location.FilePath, Is.EqualTo(GetThisFilePath()));
        Assert.That(resolved.IsExactMatch, Is.True);
        Assert.That(sourceMismatch, Is.False);
    }

    [Test]
    public void UnknownDocumentTest() {
        // A file the PDB has no equally named document for is not a mismatch
        var unknownPath = Path.Combine(tempDirectory, "Unknown.cs");
        File.WriteAllText(unknownPath, "class Unknown { void Method() { } }");

        Assert.That(reader.ResolveBreakpoint(unknownPath, 1, null, requireExactSource: true, out var sourceMismatch), Is.Null);
        Assert.That(sourceMismatch, Is.False);
    }

    private static string GetThisFilePath([CallerFilePath] string filePath = "") {
        return filePath;
    }
}
