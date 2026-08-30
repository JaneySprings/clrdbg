using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Locals captured by a lambda or hoisted into an async state machine live on a compiler generated class;
// the scope must list them under their original names, with the user's 'this' unwrapped from the closure
public class ClosureVariableTests : BaseDebugTestFixture {
    public ClosureVariableTests() : base(nameof(ClosureVariableTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var calculator = new Calculator(5);
        var applied = calculator.Apply(new[] { 1, 2, 3 }, 10);
        var summed = await calculator.SumAsync(new[] { 4, 5 });
        Console.WriteLine(applied + summed); // marker:end

        public class Calculator {
            private readonly int factor;

            public Calculator(int factor) {
                this.factor = factor;
            }

            public int Apply(int[] values, int offset) {
                var scale = 2;
                Func<int, int> transform = value => {
                    return value * factor + offset * scale; // marker:insideLambda
                };
                return transform(values[0]);
            }

            public async Task<int> SumAsync(int[] values) {
                var total = 0;
                foreach (var value in values) {
                    await Task.Delay(1);
                    total += value; // marker:insideAsync
                }
                return total;
            }
        }
        """;
    }

    [Test]
    public void LambdaScopeShowsCapturedVariablesTest() {
        var threadId = LaunchToMarker("marker:insideLambda");
        var locals = GetLocalVariables(threadId);

        Assert.That(locals.Select(it => it.Name), Does.Contain("value [int]"), "The lambda's own parameter is listed");
        var offset = locals.First(it => it.Name == "offset [int]");
        Assert.That(offset.Value, Is.EqualTo("10"), "A captured argument of the enclosing method is listed under its own name");
        var scale = locals.First(it => it.Name == "scale [int]");
        Assert.That(scale.Value, Is.EqualTo("2"), "A captured local of the enclosing method is listed too");

        // 'this' is the user's Calculator, not the compiler's display class
        var thisVariable = locals.First(it => it.Name == "this [Calculator]");
        var members = GetVariables(thisVariable.VariablesReference);
        var nonPublicGroup = members.First(it => it.Name == "Non-Public members");
        var factor = GetVariables(nonPublicGroup.VariablesReference).First(it => it.Name == "factor [int]");
        Assert.That(factor.Value, Is.EqualTo("5"));
    }

    [Test]
    public void AsyncMethodScopeShowsHoistedLocalsTest() {
        var threadId = LaunchToMarker("marker:insideAsync");
        var locals = GetLocalVariables(threadId);

        Assert.That(locals.Select(it => it.Name), Does.Contain("this [Calculator]"), "'this' is unwrapped from the state machine");
        var values = locals.First(it => it.Name == "values [int[]]");
        Assert.That(values.Value, Is.EqualTo("{int[2]}"), "The hoisted parameter is listed under its own name");
        Assert.That(locals.First(it => it.Name == "total [int]"), Is.Not.Null, "Hoisted locals are listed");
        var value = locals.First(it => it.Name == "value [int]");
        Assert.That(value.Value, Is.EqualTo("4").Or.EqualTo("5"), "The loop variable survives the await");
    }

    [Test]
    public void EvaluateCapturedVariableTest() {
        var threadId = LaunchToMarker("marker:insideLambda");
        Assert.That(Evaluate("offset * scale", threadId).Result, Is.EqualTo("20"), "Captured variables resolve in expressions");
        Assert.That(Evaluate("factor", threadId).Result, Is.EqualTo("5"), "The captured 'this' provides the instance fields");
    }
}
