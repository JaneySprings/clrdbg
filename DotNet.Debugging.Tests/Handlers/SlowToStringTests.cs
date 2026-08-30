using System.Diagnostics;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// The implicit ToString/DebuggerDisplay evaluations of one variables request share a time budget,
// so a listing of values with a slow ToString override must not evaluate every one of them
public class SlowToStringTests : BaseDebugTestFixture {
    public SlowToStringTests() : base(nameof(SlowToStringTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var items = new List<MyClass>();
        for (var j = 0; j < 5; j++) {
            items.Add(new MyClass());
        }
        Console.WriteLine($"{items.Count}"); // marker:stop
        Console.WriteLine("done");

        public class MyClass {
            public int ToStringCount;

            // Sleeps just over the budget, so the first evaluation exhausts it alone
            public override string ToString() {
                ToStringCount++;
                Thread.Sleep(2200);
                return "slow";
            }
        }
        """;
    }

    [Test]
    public void SlowToStringBudgetTest() {
        var threadId = LaunchToMarker();
        var items = GetLocalVariables(threadId).First(it => it.Name.StartsWith("items"));
        var stopwatch = Stopwatch.StartNew();
        var members = GetVariables(items.VariablesReference);
        stopwatch.Stop();

        var first = members.First(it => it.Name == "[0] [MyClass]");
        var last = members.First(it => it.Name == "[4] [MyClass]");
        Assert.That(first.Value, Is.EqualTo("{slow}"), "The first value is formatted through its ToString override, braced like Microsoft's debugger does");
        Assert.That(last.Value, Is.EqualTo("{MyClass}"), "Values formatted after the budget ran out fall back to the type name");
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000), "One slow ToString must not stall the whole listing");

        var firstCount = GetVariables(first.VariablesReference).First(it => it.Name.StartsWith("ToStringCount"));
        var lastCount = GetVariables(last.VariablesReference).First(it => it.Name.StartsWith("ToStringCount"));
        Assert.That(firstCount.Value, Is.EqualTo("1"), "ToString ran for the first value only");
        Assert.That(lastCount.Value, Is.EqualTo("0"), "ToString must not run once the budget is exhausted");
    }
}
