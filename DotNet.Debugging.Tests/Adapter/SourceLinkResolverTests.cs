using DotNet.Debugging.Adapter;
using DotNet.Debugging.Adapter.Symbols;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class SourceLinkResolverTests {
    [Test]
    public void DefaultOptionsEnableEverythingTest() {
        var resolver = new SourceLinkResolver(new Dictionary<string, SourceLinkOptions> { ["*"] = new SourceLinkOptions() });
        Assert.That(resolver.IsEnabled("https://raw.githubusercontent.com/org/repo/sha/File.cs"), Is.True);
    }

    [Test]
    public void MostSpecificPatternWinsTest() {
        var options = new Dictionary<string, SourceLinkOptions> {
            ["*"] = new SourceLinkOptions { Enabled = true },
            ["https://raw.githubusercontent.com/*"] = new SourceLinkOptions { Enabled = false },
            ["https://raw.githubusercontent.com/trusted/*"] = new SourceLinkOptions { Enabled = true },
        };
        var resolver = new SourceLinkResolver(options);
        Assert.That(resolver.IsEnabled("https://example.com/File.cs"), Is.True);
        Assert.That(resolver.IsEnabled("https://raw.githubusercontent.com/org/repo/sha/File.cs"), Is.False);
        Assert.That(resolver.IsEnabled("https://raw.githubusercontent.com/trusted/repo/sha/File.cs"), Is.True);
    }

    [Test]
    public void SourceReferencesAreStablePerUrlTest() {
        var resolver = new SourceLinkResolver(new Dictionary<string, SourceLinkOptions> { ["*"] = new SourceLinkOptions { Enabled = true } });
        var first = resolver.GetSourceReference("https://example.com/First.cs");
        var second = resolver.GetSourceReference("https://example.com/Second.cs");
        Assert.That(first, Is.GreaterThan(0));
        Assert.That(second, Is.Not.EqualTo(first));
        Assert.That(resolver.GetSourceReference("https://example.com/First.cs"), Is.EqualTo(first), "The same document keeps its reference across stops");
    }

    [Test]
    public void DisabledUrlHasNoSourceReferenceTest() {
        var resolver = new SourceLinkResolver(new Dictionary<string, SourceLinkOptions> { ["*"] = new SourceLinkOptions { Enabled = false } });
        Assert.That(resolver.GetSourceReference("https://example.com/Missing.cs"), Is.Zero);
    }

    [Test]
    public void UnknownReferenceHasNoContentTest() {
        var resolver = new SourceLinkResolver(new Dictionary<string, SourceLinkOptions> { ["*"] = new SourceLinkOptions() });
        Assert.That(resolver.GetSourceContent(12345), Is.Null);
    }

    [Test]
    public void FailedDownloadIsReportedOnceTest() {
        var resolver = new SourceLinkResolver(new Dictionary<string, SourceLinkOptions> { ["*"] = new SourceLinkOptions() });
        var reference = resolver.GetSourceReference("http://127.0.0.1:1/Missing.cs");

        Assert.That(resolver.GetSourceContent(reference), Is.Null);
        Assert.That(resolver.GetSourceContent(reference), Is.Null, "A failed download is not retried");
    }
}
