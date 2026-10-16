using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A local function is compiled to a method of the enclosing type named '<Main>g__PrintMe|0_0', and the expression
// compiler binds metadata by name: an expression cannot call a local function by its source name, in a top-level
// program or in an explicit Main alike
public class LocalFunctionEvaluationTests : BaseDebugTestFixture {
    private const string NotInContext = "error CS0103: The name '{0}' does not exist in the current context";

    public LocalFunctionEvaluationTests() : base(nameof(LocalFunctionEvaluationTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var count = 3;
        PrintMe("Hello, World!"); // marker:top-level
        Console.WriteLine(Twice(count));
        Worker.Run();

        static int PrintMe(string text) {
            Console.WriteLine(text);
            return 1;
        }

        int Twice(int value) {
            return value * 2 + count;
        }

        static class Worker {
            public static void Run() {
                var count = 5;
                Console.WriteLine(Local(count)); // marker:method

                static int Local(int value) => value + 1;
            }
        }
        """;
    }

    [Test]
    public void LocalFunctionsOfTopLevelStatementsAreNotCallableTest() {
        var threadId = LaunchToMarker("marker:top-level");

        Assert.Multiple(() => {
            Assert.That(Evaluate("count", threadId).Result, Is.EqualTo("3"));
            Assert.That(Assert.Throws<ProtocolException>(() => Evaluate("PrintMe(\"22\")", threadId))!.Message, Is.EqualTo(string.Format(NotInContext, "PrintMe")));
            Assert.That(Assert.Throws<ProtocolException>(() => Evaluate("Twice(5)", threadId))!.Message, Is.EqualTo(string.Format(NotInContext, "Twice")));
        });
    }

    [Test]
    public void LocalFunctionsOfAMethodAreNotCallableTest() {
        var threadId = LaunchToMarker("marker:method");

        Assert.Multiple(() => {
            Assert.That(Evaluate("count", threadId).Result, Is.EqualTo("5"));
            Assert.That(Assert.Throws<ProtocolException>(() => Evaluate("Local(1)", threadId))!.Message, Is.EqualTo(string.Format(NotInContext, "Local")));
        });
    }
}
