using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Frame values captured before a func eval are used after it: a local's value stays usable across the
// evaluations run on the way (its DebuggerDisplay, a call earlier in the same expression)
public class FuncEvalLifetimeTests : BaseDebugTestFixture {
    public FuncEvalLifetimeTests() : base(nameof(FuncEvalLifetimeTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var point = new Point(3, 4);
        var item = new Item(7);
        var points = new[] { new Point(1, 2), new Point(5, 6) };
        Console.WriteLine($"{point.X} {item.Number} {points.Length}"); // marker:stop
        Console.WriteLine("done");

        [System.Diagnostics.DebuggerDisplay("{Describe()}")]
        public struct Point {
            public int X;
            public int Y;

            public Point(int x, int y) {
                X = x;
                Y = y;
            }
            public string Describe() => $"P{X}";
        }
        public class Item {
            public int Number { get; }

            public Item(int number) {
                Number = number;
            }
            public int Doubled() => Number * 2;
        }
        """;
    }

    [Test]
    public void StructLocalExpandsAfterItsDisplayEvaluationTest() {
        var threadId = LaunchToMarker();
        var point = GetLocalVariables(threadId).First(it => it.Name.StartsWith("point "));
        Assert.That(point.Value, Is.EqualTo("P3"), "The DebuggerDisplay ran a func eval on the struct");
        Assert.That(point.VariablesReference, Is.GreaterThan(0), "The struct is still expandable after that eval");

        var members = GetVariables(point.VariablesReference);
        Assert.That(members.First(it => it.Name.StartsWith("X [")).Value, Is.EqualTo("3"));
        Assert.That(members.First(it => it.Name.StartsWith("Y [")).Value, Is.EqualTo("4"));
    }

    [Test]
    public void ArrayElementsExpandAfterTheirDisplayEvaluationsTest() {
        var threadId = LaunchToMarker();
        var points = GetLocalVariables(threadId).First(it => it.Name.StartsWith("points"));
        var elements = GetVariables(points.VariablesReference);
        Assert.That(elements.Select(it => it.Value), Is.EqualTo(new[] { "P1", "P5" }), "The second element is read after the first one's eval");
        Assert.That(GetVariables(elements[1].VariablesReference).First(it => it.Name.StartsWith("X [")).Value, Is.EqualTo("5"));
    }

    [Test]
    public void LocalReadAfterCallInTheSameExpressionTest() {
        var threadId = LaunchToMarker();
        Assert.That(Evaluate("item.Doubled() + point.X", threadId).Result, Is.EqualTo("17"), "A struct local read after a call");
        Assert.That(Evaluate("item.Doubled() + item.Number", threadId).Result, Is.EqualTo("21"), "A reference local read after a call");
        Assert.That(Evaluate("point.Describe() + point.Y", threadId).Result, Is.EqualTo("\"P34\""));
    }
}
