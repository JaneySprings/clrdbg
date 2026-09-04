using System.Diagnostics;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Listing an array costs nothing per element: ten million of them are one block in the listing, and a page
// names, reads and formats its own elements only
public class LargeArrayPagingTests : BaseDebugTestFixture {
    private const int PageSize = 25;

    public LargeArrayPagingTests() : base(nameof(LargeArrayPagingTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var big = new int[10_000_000];
        big[0] = 7;
        big[^1] = 9;
        Console.WriteLine($"{big.Length}"); // marker:stop
        Console.WriteLine("done");
        """;
    }

    [Test]
    public void FirstPageOfHugeArrayIsCheapTest() {
        var threadId = LaunchToMarker();
        var big = GetLocalVariables(threadId).First(it => it.Name.StartsWith("big"));
        var stopwatch = Stopwatch.StartNew();
        var firstPage = GetVariables(big.VariablesReference);
        stopwatch.Stop();

        Assert.That(firstPage.Count(it => it.Name != "[More]"), Is.EqualTo(PageSize));
        Assert.That(firstPage[0].Name, Does.StartWith("[0]"));
        Assert.That(firstPage[0].Value, Is.EqualTo("7"));
        Assert.That(firstPage[^1].Name, Is.EqualTo("[More]"));
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), "The page must not pay for the elements it does not show");

        var secondPage = GetVariables(firstPage[^1].VariablesReference);
        Assert.That(secondPage[0].Name, Does.StartWith("[25]"));
        Assert.That(Evaluate("big[9999999]", threadId).Result, Is.EqualTo("9"));
    }
}
