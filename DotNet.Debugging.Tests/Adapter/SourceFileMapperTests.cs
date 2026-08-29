using DotNet.Debugging.Adapter.Symbols;
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
    [TestCase("/home/elsewhere/Program.cs", "/home/elsewhere/Program.cs")]
    public void ToLocalPathTest(string compilerPath, string expected) {
        Assert.That(CreateMapper().ToLocalPath(compilerPath), Is.EqualTo(expected));
    }

    // The reverse direction restores the compile-time form, including its separator flavor
    [TestCase("/Users/dev/project/Program.cs", "C:\\build\\src\\Program.cs")]
    [TestCase("/Users/dev/project/Folder/File.cs", "C:\\build\\src\\Folder\\File.cs")]
    [TestCase("/Users/dev/generated/File.g.cs", "C:\\build\\src\\generated\\File.g.cs")]
    [TestCase("/Users/dev/checkout/Lib/Helper.cs", "/vsts/work/1/s/Lib/Helper.cs")]
    [TestCase("/Users/dev/projectother/Program.cs", "/Users/dev/projectother/Program.cs")]
    [TestCase("/home/elsewhere/Program.cs", "/home/elsewhere/Program.cs")]
    public void ToCompilerPathTest(string localPath, string expected) {
        Assert.That(CreateMapper().ToCompilerPath(localPath), Is.EqualTo(expected));
    }
}
