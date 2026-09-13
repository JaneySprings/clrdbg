using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A parameter captured by a lambda lives on the display class from the method's first statement on: the frame's
// own slot keeps the value the method was entered with. The scope lists the name once, with the live copy
public class CapturedParameterTests : BaseDebugTestFixture {
    public CapturedParameterTests() : base(nameof(CapturedParameterTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        Accumulate(5);
        Console.WriteLine("end");

        static void Accumulate(int total) {
            Action add = () => total += 10;
            add();
            Console.WriteLine(total); // marker:afterAdd
            Func<int> nested = () => {
                var doubled = total * 2;
                Func<int> inner = () => doubled + total;
                return inner(); // marker:insideNested
            };
            Console.WriteLine(nested());
        }
        """;
    }

    [Test]
    public void CapturedParameterShowsTheClosureCopyTest() {
        var threadId = LaunchToMarker("marker:afterAdd");
        var locals = GetLocalVariables(threadId);

        var total = locals.Where(it => it.Name == "total [int]").ToList();
        Assert.That(total, Has.Count.EqualTo(1), "A captured parameter is listed once");
        Assert.That(total[0].Value, Is.EqualTo("15"), "The value the lambda changed is on the closure, the frame's slot is stale");
        Assert.That(Evaluate("total", threadId).Result, Is.EqualTo("15"));
    }

    // An assignment goes to the closure copy as well, the one the method and its lambdas read
    [Test]
    public void SetCapturedParameterWritesTheClosureCopyTest() {
        var threadId = LaunchToMarker("marker:afterAdd");
        var scopes = Host.SendRequestSync(new ScopesRequest() { FrameId = GetTopStackFrame(threadId).Id });
        var response = Host.SendRequestSync(new SetVariableRequest() {
            VariablesReference = scopes.Scopes[0].VariablesReference,
            Name = "total [int]",
            Value = "40",
        });

        Assert.That(response.Value, Is.EqualTo("40"));
        Assert.That(Evaluate("total", threadId).Result, Is.EqualTo("40"));
        Assert.That(GetLocalVariables(threadId).Single(it => it.Name == "total [int]").Value, Is.EqualTo("40"));
    }

    [Test]
    public void NestedLambdaListsEveryCapturedNameOnceTest() {
        var threadId = LaunchToMarker("marker:insideNested");
        var names = GetLocalVariables(threadId).Select(it => it.Name).ToList();

        Assert.That(names.Count(it => it == "total [int]"), Is.EqualTo(1), "The enclosing closure's variable comes once");
        Assert.That(names.Count(it => it == "doubled [int]"), Is.EqualTo(1));
        Assert.That(names.Count(it => it == "inner [Func<int>]"), Is.EqualTo(1));
        Assert.That(names.Any(it => it.StartsWith("CS$")), Is.False);
    }
}
