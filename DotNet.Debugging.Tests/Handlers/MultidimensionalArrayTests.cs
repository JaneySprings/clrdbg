using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A multidimensional array lists one row per element, named by its index in every dimension the way
// vsdbg does ('[0, 0]', '[0, 1]', ...), and pages like a single dimensional one. Element access in
// expressions compiles to the array type's pseudo methods, which only exist inside the runtime -
// the interpreter performs the access on the debug value instead (Evaluation/CilInterpreter)
public class MultidimensionalArrayTests : BaseDebugTestFixture {
    public MultidimensionalArrayTests() : base(nameof(MultidimensionalArrayTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var data = new int[2, 3];
        for (var i = 0; i < 2; i++) {
            for (var j = 0; j < 3; j++)
                data[i, j] = i * 10 + j;
        }

        var cube = new int[2, 2, 2];
        cube[1, 0, 1] = 42;

        var grid = new Cell[2, 2];
        grid[1, 1] = new Cell(7, 9);

        var wide = new int[6, 5];
        wide[5, 4] = 77;

        var shifted = Array.CreateInstance(typeof(int), new[] { 2 }, new[] { 5 });
        shifted.SetValue(55, 5);

        Console.WriteLine($"{data[1, 2]} {cube[1, 0, 1]} {grid[1, 1].X} {wide[5, 4]} {shifted.GetValue(5)}"); // marker:stop

        struct Cell {
            public int X;
            public int Y;
            public Cell(int x, int y) { X = x; Y = y; }
        }
        """;
    }

    [Test]
    public void ListsTheElementsWithTheirMultidimensionalIndices() {
        var threadId = LaunchToMarker("marker:stop");
        var data = GetLocalVariables(threadId).First(it => it.Name == "data [int[,]]");
        Assert.That(data.Value, Is.EqualTo("{int[2, 3]}"));

        var elements = GetVariables(data.VariablesReference);
        Assert.That(elements.Select(it => it.Name), Is.EqualTo(new[] { "[0, 0] [int]", "[0, 1] [int]", "[0, 2] [int]", "[1, 0] [int]", "[1, 1] [int]", "[1, 2] [int]" }));
        Assert.That(elements.Select(it => it.Value), Is.EqualTo(new[] { "0", "1", "2", "10", "11", "12" }));
    }

    [Test]
    public void PagesALongListingLikeASingleDimensionalArray() {
        var threadId = LaunchToMarker("marker:stop");
        var wide = GetLocalVariables(threadId).First(it => it.Name == "wide [int[,]]");
        var firstPage = GetVariables(wide.VariablesReference);
        Assert.That(firstPage.Count(it => it.Name != "[More]"), Is.EqualTo(25));
        Assert.That(firstPage[0].Name, Is.EqualTo("[0, 0] [int]"));

        var more = firstPage.Last();
        Assert.That(more.Name, Is.EqualTo("[More]"), "A listing longer than a page ends with the node opening the next one");
        var secondPage = GetVariables(more.VariablesReference);
        Assert.That(secondPage.Select(it => it.Name), Does.Contain("[5, 4] [int]"));
        Assert.That(secondPage.First(it => it.Name == "[5, 4] [int]").Value, Is.EqualTo("77"));
    }

    [Test]
    public void EvaluatesElementsByTheirIndices() {
        var threadId = LaunchToMarker("marker:stop");
        Assert.That(Evaluate("data[1, 2]", threadId).Result, Is.EqualTo("12"));
        Assert.That(Evaluate("cube[1, 0, 1]", threadId).Result, Is.EqualTo("42"));
        Assert.That(Evaluate("data[0, 1] + data[1, 0]", threadId).Result, Is.EqualTo("11"));
    }

    // Reading a member of a struct element goes through the array type's 'Address' pseudo method
    [Test]
    public void EvaluatesAMemberOfAStructElement() {
        var threadId = LaunchToMarker("marker:stop");
        Assert.That(Evaluate("grid[1, 1].X", threadId).Result, Is.EqualTo("7"));
    }

    // Clients send the display name back when the user edits a variable
    [Test]
    public void SetsAnElementThroughItsDisplayName() {
        var threadId = LaunchToMarker("marker:stop");
        var data = GetLocalVariables(threadId).First(it => it.Name == "data [int[,]]");
        var response = Host.SendRequestSync(new SetVariableRequest() {
            VariablesReference = data.VariablesReference,
            Name = "[1, 2] [int]",
            Value = "99",
        });
        Assert.That(response.Value, Is.EqualTo("99"));
        Assert.That(Evaluate("data[1, 2]", threadId).Result, Is.EqualTo("99"));
    }

    // An array created with a lower bound names its elements by the logical index, the way vsdbg does
    [Test]
    public void ListsTheElementsFromTheLowerBound() {
        var threadId = LaunchToMarker("marker:stop");
        var shifted = GetLocalVariables(threadId).First(it => it.Name.StartsWith("shifted"));
        var elements = GetVariables(shifted.VariablesReference);
        Assert.That(elements.Select(it => it.Name), Is.EqualTo(new[] { "[5] [int]", "[6] [int]" }));
        Assert.That(elements[0].Value, Is.EqualTo("55"));
    }
}
