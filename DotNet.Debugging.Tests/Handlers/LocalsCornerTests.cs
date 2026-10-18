using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Which variables the Locals scope lists in the shapes the compiler rewrites: hoisted locals of a state machine, a
// local function's closure, primary constructor parameters, named tuple elements
public class LocalsCornerTests : BaseDebugTestFixture {
    public LocalsCornerTests() : base(nameof(LocalsCornerTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var cases = new Cases();
        await cases.HoistedAsync(5);
        foreach (var item in cases.Iterate(2))
            Console.WriteLine(item);
        cases.LocalFunction(4);
        cases.Tuples();
        new Primary("p", 42).Show();
        Console.WriteLine("done"); // marker:end

        public class Cases {
            public async Task HoistedAsync(int count) {
                int before = count + 1;
                Console.WriteLine(before); // marker:async-before
                await Task.Yield();
                for (int i = 0; i < 2; i++) {
                    await Task.Yield();
                    Console.WriteLine(i); // marker:async-loop
                }
            }
            public IEnumerable<int> Iterate(int count) {
                int produced = 0;
                for (int i = 0; i < count; i++) {
                    produced += i;
                    yield return produced; // marker:iterator
                }
            }
            public void LocalFunction(int seed) {
                int shared = seed;
                Console.WriteLine(Twice(3));

                int Twice(int value) {
                    int local = value * 2 + shared;
                    return local; // marker:local-function
                }
            }
            public void Tuples() {
                var pair = (Name: "n", Count: 3);
                (int x, string y) typed = (1, "one");
                Console.WriteLine(pair.Count + typed.x); // marker:tuples
            }
        }
        public class Primary(string name, int age) {
            public void Show() {
                var description = $"{name} {age}";
                Console.WriteLine(description); // marker:primary
            }
        }
        """;
    }

    // A local hoisted into the state machine is listed only where its scope holds the current offset, as a plain
    // local would be: the loop variable appears inside the loop, not before it. The compiler records the scopes of the
    // hoisted locals in the PDB, the state machine's fields alone cannot tell
    [Test]
    public void HoistedLocalsFollowTheirScopesTest() {
        var threadId = LaunchToMarker("marker:async-before");
        Assert.That(GetLocalVariables(threadId).Select(it => it.Name), Is.EqualTo(new[] { "this [Cases]", "count [int]", "before [int]" }));
        SetBreakpoints(GetMarkerLine("marker:async-loop"));
        Continue(threadId);
        threadId = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint).ThreadId!.Value;
        Assert.That(GetLocalVariables(threadId).Select(it => it.Name), Is.EqualTo(new[] { "this [Cases]", "count [int]", "before [int]", "i [int]" }));
        // The 'item' of Main's foreach is hoisted too, and out of scope once the loop is left
        SetBreakpoints(GetMarkerLine("marker:end"));
        Continue(threadId);
        threadId = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint).ThreadId!.Value;
        Assert.That(GetLocalVariables(threadId).Select(it => it.Name), Is.EqualTo(new[] { "args [string[]]", "cases [Cases]" }));
    }

    // The properties an iterator implements for the runtime ('IEnumerator<int>.Current') live on the same state machine
    // as the hoisted locals; they are not the method's variables
    [Test]
    public void IteratorListsHoistedLocalsOnlyTest() {
        var threadId = LaunchToMarker("marker:iterator");
        Assert.That(GetLocalVariables(threadId).Select(it => it.Name), Is.EqualTo(new[] { "this [Cases]", "count [int]", "produced [int]", "i [int]" }));
    }

    // A local function receives the variables it captured in a struct closure passed by reference, as an unnamed
    // argument: its members are the function's variables
    [Test]
    public void LocalFunctionListsCapturedVariablesTest() {
        var threadId = LaunchToMarker("marker:local-function");
        Assert.That(GetLocalVariables(threadId).Select(it => it.Name), Is.EqualTo(new[] { "value [int]", "shared [int]", "local [int]" }));
        Assert.That(GetLocalVariables(threadId).Single(it => it.Name == "shared [int]").Value, Is.EqualTo("4"));
    }

    // The primary constructor parameters a type keeps as fields are in scope throughout its members, like locals
    [Test]
    public void PrimaryConstructorParametersAreListedTest() {
        var threadId = LaunchToMarker("marker:primary");
        var locals = GetLocalVariables(threadId);
        Assert.That(locals.Select(it => it.Name), Is.EqualTo(new[] { "this [Primary]", "name [string]", "age [int]", "description [string]" }));
        Assert.That(locals.Single(it => it.Name == "age [int]").Value, Is.EqualTo("42"));
        // Inside 'this' the fields keep their place among the non-public members, under their source names
        var nonPublic = GetVariables(locals[0].VariablesReference).Single(it => it.Name == "Non-Public members");
        Assert.That(GetVariables(nonPublic.VariablesReference).Select(it => it.Name), Is.EqualTo(new[] { "age [int]", "name [string]" }));
    }

    // A tuple local names its elements in the PDB: the children show those names instead of Item1, Item2
    [Test]
    public void TupleLocalsShowElementNamesTest() {
        var threadId = LaunchToMarker("marker:tuples");
        var locals = GetLocalVariables(threadId);
        var pair = GetVariables(locals.Single(it => it.Name.StartsWith("pair ")).VariablesReference);
        Assert.That(pair.Select(it => it.Name), Is.EqualTo(new[] { "Count [int]", "Name [string]", "Non-Public members" }));
        Assert.That(pair.Single(it => it.Name == "Name [string]").Value, Is.EqualTo("\"n\""));
        var typed = GetVariables(locals.Single(it => it.Name.StartsWith("typed ")).VariablesReference);
        Assert.That(typed.Select(it => it.Name), Is.EqualTo(new[] { "x [int]", "y [string]", "Non-Public members" }));
    }
}
