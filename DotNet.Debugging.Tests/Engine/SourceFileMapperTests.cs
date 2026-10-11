using DotNet.Debugging.Engine.Metadata;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class SourceFileMapperTests {
    private static SourceFileMapper CreateMapper() {
        return new SourceFileMapper(new Dictionary<string, string> {
            ["C:\\build\\src"] = "/Users/dev/project",
            ["C:\\build\\src\\generated"] = "/Users/dev/generated",
            ["/vsts/work/1/s"] = "/Users/dev/checkout/",
        });
    }

    [TestCase("C:\\build\\src\\Program.cs", "/Users/dev/project/Program.cs")]
    [TestCase("C:\\build\\src\\Folder\\File.cs", "/Users/dev/project/Folder/File.cs")]
    [TestCase("c:\\BUILD\\src\\Program.cs", "/Users/dev/project/Program.cs")]
    [TestCase("C:/build/src/Program.cs", "/Users/dev/project/Program.cs")]
    // The longest matching prefix wins
    [TestCase("C:\\build\\src\\generated\\File.g.cs", "/Users/dev/generated/File.g.cs")]
    // A prefix only matches on a directory boundary
    [TestCase("C:\\build\\srcother\\Program.cs", "C:\\build\\srcother\\Program.cs")]
    // A trailing separator in the map entry does not matter
    [TestCase("/vsts/work/1/s/Lib/Helper.cs", "/Users/dev/checkout/Lib/Helper.cs")]
    // A document no entry applies to was built where it is
    [TestCase("/home/elsewhere/Program.cs", "/home/elsewhere/Program.cs")]
    [TestCase("/Users/dev/project/Program.cs", "/Users/dev/project/Program.cs")]
    public void ToLocalPathTest(string compilerPath, string expected) {
        Assert.That(CreateMapper().ToLocalPath(compilerPath), Is.EqualTo(expected));
    }
}
