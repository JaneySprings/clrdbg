using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A listing longer than one page is handed out through '[More]' nodes, and only the page the client asked for
// is read from the debuggee - expanding a large collection must not evaluate every element it holds
public class VariablePagingTests : BaseDebugTestFixture {
    private const int PageSize = 25;
    private const int ItemCount = 60;

    public VariablePagingTests() : base(nameof(VariablePagingTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var items = new List<Item>();
        for (var j = 0; j < 60; j++) {
            items.Add(new Item(j));
        }
        var wide = new Wide();
        Console.WriteLine($"{items.Count}{wide.P00}"); // marker:stop
        Console.WriteLine("done");

        public class Item {
            public static int ToStringCount;
            private readonly int index;

            public Item(int index) {
                this.index = index;
            }
            public override string ToString() {
                ToStringCount++;
                return $"item {index}";
            }
        }

        // Thirty properties, each one a func eval when it is read
        public class Wide {
            public static int GetterCount;

            public int P00 { get { GetterCount++; return 0; } }
            public int P01 { get { GetterCount++; return 1; } }
            public int P02 { get { GetterCount++; return 2; } }
            public int P03 { get { GetterCount++; return 3; } }
            public int P04 { get { GetterCount++; return 4; } }
            public int P05 { get { GetterCount++; return 5; } }
            public int P06 { get { GetterCount++; return 6; } }
            public int P07 { get { GetterCount++; return 7; } }
            public int P08 { get { GetterCount++; return 8; } }
            public int P09 { get { GetterCount++; return 9; } }
            public int P10 { get { GetterCount++; return 10; } }
            public int P11 { get { GetterCount++; return 11; } }
            public int P12 { get { GetterCount++; return 12; } }
            public int P13 { get { GetterCount++; return 13; } }
            public int P14 { get { GetterCount++; return 14; } }
            public int P15 { get { GetterCount++; return 15; } }
            public int P16 { get { GetterCount++; return 16; } }
            public int P17 { get { GetterCount++; return 17; } }
            public int P18 { get { GetterCount++; return 18; } }
            public int P19 { get { GetterCount++; return 19; } }
            public int P20 { get { GetterCount++; return 20; } }
            public int P21 { get { GetterCount++; return 21; } }
            public int P22 { get { GetterCount++; return 22; } }
            public int P23 { get { GetterCount++; return 23; } }
            public int P24 { get { GetterCount++; return 24; } }
            public int P25 { get { GetterCount++; return 25; } }
            public int P26 { get { GetterCount++; return 26; } }
            public int P27 { get { GetterCount++; return 27; } }
            public int P28 { get { GetterCount++; return 28; } }
            public int P29 { get { GetterCount++; return 29; } }
        }
        """;
    }

    [Test]
    public void CollectionPageIsEvaluatedAloneTest() {
        var threadId = LaunchToMarker();
        var items = GetLocalVariables(threadId).First(it => it.Name.StartsWith("items"));

        var firstPage = GetVariables(items.VariablesReference);
        var more = firstPage[^1];
        Assert.That(more.Name, Is.EqualTo("[More]"), "A listing longer than a page ends with the node opening the next one");
        Assert.That(firstPage.Count(it => it.Name.StartsWith('[') && it.Name != "[More]"), Is.EqualTo(PageSize), "The page holds one page worth of elements");
        Assert.That(Evaluate("Item.ToStringCount", threadId).Result, Is.EqualTo(PageSize.ToString()), "Only the elements of the requested page are formatted");

        var secondPage = GetVariables(more.VariablesReference);
        Assert.That(secondPage.Count(it => it.Name.StartsWith('[') && it.Name != "[More]"), Is.EqualTo(PageSize), "Expanding '[More]' reads the next page only");
        Assert.That(Evaluate("Item.ToStringCount", threadId).Result, Is.EqualTo((2 * PageSize).ToString()), "The second page formats its own elements and nothing beyond them");

        var namesSoFar = firstPage.Concat(secondPage).Where(it => it.Name != "[More]").Select(it => it.Name).ToList();
        Assert.That(namesSoFar, Is.Unique, "The pages do not overlap");
    }

    [Test]
    public void LastPageHasNoMoreNodeTest() {
        var threadId = LaunchToMarker();
        var items = GetLocalVariables(threadId).First(it => it.Name.StartsWith("items"));

        var names = new List<string>();
        var reference = items.VariablesReference;
        var pageCount = 0;
        while (reference != 0) {
            var page = GetVariables(reference);
            pageCount++;
            var more = page.FirstOrDefault(it => it.Name == "[More]");
            names.AddRange(page.Where(it => it.Name != "[More]").Select(it => it.Name));
            reference = more?.VariablesReference ?? 0;
        }

        Assert.That(pageCount, Is.EqualTo(3), "Sixty elements and the 'Raw View' group span three pages");
        Assert.That(names.Count(it => it.StartsWith('[')), Is.EqualTo(ItemCount), "Paging through the listing yields every element exactly once");
        Assert.That(names, Does.Contain("Raw View"), "The groups closing the listing are reachable on the last page");
        Assert.That(names, Is.Unique);
    }

    [Test]
    public void ElementsAreInIndexOrderTest() {
        var threadId = LaunchToMarker();
        var items = GetLocalVariables(threadId).First(it => it.Name.StartsWith("items"));

        var firstPage = GetVariables(items.VariablesReference);
        var expected = Enumerable.Range(0, PageSize).Select(it => $"[{it}] [Item]").ToList();
        Assert.That(firstPage.Take(PageSize).Select(it => it.Name), Is.EqualTo(expected), "The elements of a proxied collection are ordered by index, not lexicographically");

        var more = firstPage.First(it => it.Name == "[More]");
        var secondPage = GetVariables(more.VariablesReference);
        Assert.That(secondPage[0].Name, Is.EqualTo($"[{PageSize}] [Item]"), "The next page continues at the following index");
    }

    [Test]
    public void MemberPageIsEvaluatedAloneTest() {
        var threadId = LaunchToMarker();
        var wide = GetLocalVariables(threadId).First(it => it.Name.StartsWith("wide"));
        // The property read by the marker line itself already ran one getter
        var before = int.Parse(Evaluate("Wide.GetterCount", threadId).Result);

        var members = GetVariables(wide.VariablesReference);
        Assert.That(members.Any(it => it.Name == "[More]"), "Thirty properties do not fit into one page");

        var after = int.Parse(Evaluate("Wide.GetterCount", threadId).Result);
        Assert.That(after - before, Is.EqualTo(PageSize), "A property outside the requested page is never invoked");
    }
}
