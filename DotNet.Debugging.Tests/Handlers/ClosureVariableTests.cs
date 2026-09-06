using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
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
        var late = await calculator.LateClosureAsync(1);
        Console.WriteLine(applied + summed + late); // marker:end

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
                return transform(values[0]); // marker:beforeCall
            }

            public async Task<int> SumAsync(int[] values) {
                var total = 0;
                foreach (var value in values) {
                    await Task.Delay(1);
                    total += value; // marker:insideAsync
                }
                return total;
            }

            public async Task<int> LateClosureAsync(int n) {
                await Task.Delay(1);
                var doubled = n * 2; // marker:beforeClosure
                if (n > 0) {
                    var captured = n;
                    Action print = () => Console.WriteLine(captured);
                    await Task.Delay(1);
                    print();
                }
                return doubled;
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

    // The method declaring the lambda keeps its captured variables on a display class the compiler leaves visible
    // as a local: the scope shows the variables, each once, and never the class
    [Test]
    public void DeclaringMethodScopeShowsCapturedVariablesTest() {
        var threadId = LaunchToMarker("marker:beforeCall");
        var locals = GetLocalVariables(threadId);
        var names = locals.Select(it => it.Name).ToList();

        Assert.That(names.Any(it => it.StartsWith("CS$")), Is.False, "The display class local is not listed");
        Assert.That(locals.First(it => it.Name == "scale [int]").Value, Is.EqualTo("2"), "The captured local is listed from the display class");
        Assert.That(names.Count(it => it == "offset [int]"), Is.EqualTo(1), "A captured parameter is listed once");
        Assert.That(names, Does.Contain("values [int[]]").And.Contain("transform [Func<int, int>]").And.Contain("this [Calculator]"));
    }

    // A local the compiler moved onto the state machine or a closure is assigned where it lives
    [Test]
    public void SetHoistedLocalTest() {
        var threadId = LaunchToMarker("marker:insideAsync");
        Assert.That(SetLocal(threadId, "total [int]", "100"), Is.EqualTo("100"));
        Assert.That(Evaluate("total", threadId).Result, Is.EqualTo("100"));
    }

    [Test]
    public void SetCapturedLocalTest() {
        var threadId = LaunchToMarker("marker:insideLambda");
        Assert.That(SetLocal(threadId, "scale [int]", "3"), Is.EqualTo("3"), "From the lambda, on its closure");
        Assert.That(Evaluate("offset * scale", threadId).Result, Is.EqualTo("30"));
    }

    [Test]
    public void SetCapturedLocalFromDeclaringMethodTest() {
        var threadId = LaunchToMarker("marker:beforeCall");
        Assert.That(SetLocal(threadId, "scale [int]", "4"), Is.EqualTo("4"), "From the declaring method, on its display class local");
        Assert.That(Evaluate("scale", threadId).Result, Is.EqualTo("4"));
    }

    private string SetLocal(int threadId, string name, string value) {
        var scopes = Host.SendRequestSync(new ScopesRequest() { FrameId = GetTopStackFrame(threadId).Id });
        var response = Host.SendRequestSync(new SetVariableRequest() {
            VariablesReference = scopes.Scopes[0].VariablesReference,
            Name = name,
            Value = value,
        });
        return response.Value;
    }

    // A closure declared in a block the method has not reached yet is a null field of the state machine
    [Test]
    public void AsyncScopeBeforeTheClosureExistsTest() {
        var threadId = LaunchToMarker("marker:beforeClosure");
        var locals = GetLocalVariables(threadId);

        Assert.That(locals.First(it => it.Name == "n [int]").Value, Is.EqualTo("1"));
        Assert.That(locals.Select(it => it.Name), Does.Not.Contain("captured [int]"), "A variable of a closure not created yet is not in scope");
    }
}
