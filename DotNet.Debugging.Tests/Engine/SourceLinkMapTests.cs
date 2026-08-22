using DotNet.Debugging.Engine.Metadata;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class SourceLinkMapTests {
    private const string Json = """
        {
            "documents": {
                "C:\\src\\repo\\*": "https://raw.githubusercontent.com/org/repo/abc123/*",
                "/_/*": "https://raw.githubusercontent.com/org/other/def456/*",
                "/_/src/Special.cs": "https://example.com/special/Special.cs",
                "/_/src/lib/*": "https://example.com/lib/*"
            }
        }
        """;

    [TestCase("C:\\src\\repo\\Program.cs", "https://raw.githubusercontent.com/org/repo/abc123/Program.cs")]
    [TestCase("C:\\src\\repo\\Folder\\File.cs", "https://raw.githubusercontent.com/org/repo/abc123/Folder/File.cs")]
    [TestCase("c:/SRC/repo/Program.cs", "https://raw.githubusercontent.com/org/repo/abc123/Program.cs")]
    [TestCase("/_/src/Program.cs", "https://raw.githubusercontent.com/org/other/def456/src/Program.cs")]
    [TestCase("/_/src/lib/Util.cs", "https://example.com/lib/Util.cs")]
    [TestCase("/_/src/Special.cs", "https://example.com/special/Special.cs")]
    [TestCase("/home/user/Program.cs", null)]
    public void GetUrlTest(string documentPath, string? expected) {
        var map = SourceLinkMap.TryParse(Json);
        Assert.That(map, Is.Not.Null);
        Assert.That(map!.GetUrl(documentPath), Is.EqualTo(expected));
    }

    [TestCase("not json")]
    [TestCase("{}")]
    [TestCase("{\"documents\": {}}")]
    [TestCase("{\"documents\": \"oops\"}")]
    public void InvalidJsonTest(string json) {
        Assert.That(SourceLinkMap.TryParse(json), Is.Null);
    }
}
