using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A 'using', a 'lock' and a 'foreach' compile to a try/finally whose sequence points are hidden: the offsets
// belong to no source line of their own. The runtime ends a range step at the handler, and a stop there would
// report the closest earlier statement - the line the step has just left - so the step controller keeps
// stepping instead (Stepping/StepController with Metadata/ModuleMetadataReader.IsInHiddenRegion). The same
// goes for the other hidden code a step can land in: the leave out of a try guarded by a finally once its catch
// blocks are passed, and the dispatch of a pattern switch after a failed 'when' clause
public class HiddenRegionSteppingTests : BaseDebugTestFixture {
    public HiddenRegionSteppingTests() : base(nameof(HiddenRegionSteppingTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var gate = new object();

        using (var single = Resource.Open(1))
            Use(single); // marker:singleBody
        Console.WriteLine("after single"); // marker:afterSingle

        using (var braced = Resource.Open(2)) {
            Use(braced); // marker:bracedBody
        } // marker:bracedClose
        Console.WriteLine("after braced"); // marker:afterBraced

        using (var outer = Resource.Open(3))
        using (var inner = Resource.Open(4))
            Use(inner); // marker:nestedBody
        Console.WriteLine("after nested"); // marker:afterNested

        lock (gate)
            Console.WriteLine("locked"); // marker:lockBody
        Console.WriteLine("after lock"); // marker:afterLock

        foreach (var item in Items()) // marker:foreachHeader
            Console.WriteLine(item); // marker:foreachBody
        Console.WriteLine("after foreach"); // marker:afterForeach

        try {
            Console.WriteLine("guarded"); // marker:tryBody
        } // marker:tryClose
        finally { // marker:finallyOpen
            Console.WriteLine("cleanup"); // marker:finallyBody
        }
        Console.WriteLine("after finally"); // marker:afterFinally

        try {
            Console.WriteLine("guarded twice"); // marker:catchFinallyBody
        } // marker:catchFinallyClose
        catch {
            throw; // marker:rethrow
        }
        finally { // marker:catchFinallyOpen
            Console.WriteLine("cleanup twice"); // marker:catchFinallyCleanup
        }
        Console.WriteLine("after catch finally"); // marker:afterCatchFinally

        object boxed = 5;
        switch (boxed) {
            case int negative when negative < 0: // marker:negativeGuard
                Console.WriteLine("negative");
                break; // marker:negativeBreak
            case int big when big > 100: // marker:bigGuard
                Console.WriteLine("big");
                break; // marker:bigBreak
            case int other:
                Console.WriteLine($"other {other}"); // marker:otherBody
                break; // marker:otherBreak
        }
        Console.WriteLine("after switch"); // marker:afterSwitch

        RunTailUsing(); // marker:tailCall
        Console.WriteLine("after tail"); // marker:afterTail

        static void RunTailUsing() {
            using (var tail = Resource.Open(5))
                Use(tail); // marker:tailBody
        } // marker:tailClose

        static void Use(Resource resource) => Console.WriteLine($"use {resource.Id}");
        static IEnumerable<int> Items() { yield return 1; }

        sealed class Resource : IDisposable {
            public int Id { get; }
            private Resource(int id) { Id = id; }
            public static Resource Open(int id) => new Resource(id);
            public void Dispose() => Console.WriteLine($"dispose {Id}"); // marker:dispose
        }
        """;
    }

    // The reported bug: the step stopped in the hidden finally the 'using' compiles to, which maps back to
    // the body line, so the user pressed 'step over' and arrived on the line they started from
    [Test]
    public void StepOverUsingBodyReachesTheNextStatement() {
        var threadId = LaunchToMarker("marker:singleBody");
        var frame = StepOver(threadId);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:afterSingle")));
    }

    // The generated finally calls Dispose, a step over must pass through it rather than into it
    [Test]
    public void StepOverUsingBodyDoesNotEnterDispose() {
        var threadId = LaunchToMarker("marker:singleBody");
        var frame = StepOver(threadId);
        Assert.That(frame.Line, Is.Not.EqualTo(GetMarkerLine("marker:dispose")));
        Assert.That(frame.Name, Does.Contain("Main"));
    }

    [Test]
    public void StepIntoUsingBodyEntersTheCall() {
        var threadId = LaunchToMarker("marker:singleBody");
        var frame = StepIn(threadId);
        Assert.That(frame.Name, Does.Contain("Use"));
    }

    // A braced 'using' has a sequence point of its own on the closing brace, the step stops there and the
    // hidden finally behind it is passed through on the step after
    [Test]
    public void StepOverBracedUsingBodyReachesTheClosingBraceThenTheNextStatement() {
        var threadId = LaunchToMarker("marker:bracedBody");
        var closing = StepOver(threadId);
        Assert.That(closing.Line, Is.EqualTo(GetMarkerLine("marker:bracedClose")));

        var next = StepOver(threadId);
        Assert.That(next.Line, Is.EqualTo(GetMarkerLine("marker:afterBraced")));
    }

    // Two stacked 'using' statements nest two hidden finallys, the step has to pass through both
    [Test]
    public void StepOverNestedUsingBodyReachesTheNextStatement() {
        var threadId = LaunchToMarker("marker:nestedBody");
        var frame = StepOver(threadId);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:afterNested")));
    }

    [Test]
    public void StepOverLockBodyReachesTheNextStatement() {
        var threadId = LaunchToMarker("marker:lockBody");
        var frame = StepOver(threadId);
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:afterLock")));
    }

    // The body steps back to the header for the next iteration. The sequence yields a single item, so the
    // loop ends there and the step passes through the hidden finally that disposes the enumerator
    [Test]
    public void StepOverLastForeachIterationReachesTheNextStatement() {
        var threadId = LaunchToMarker("marker:foreachBody");
        var header = StepOver(threadId);
        Assert.That(header.Line, Is.EqualTo(GetMarkerLine("marker:foreachHeader")));

        var next = StepOver(threadId);
        Assert.That(next.Line, Is.EqualTo(GetMarkerLine("marker:afterForeach")));
    }

    // A step out of Dispose returns into the hidden finally that called it. The rest of that step covers
    // the region like a step over - resuming a step out there would leave the frame just stepped into
    [Test]
    public void StepOutOfDisposeReachesTheStatementAfterTheUsing() {
        var threadId = LaunchToMarker("marker:dispose");
        var frame = StepOut(threadId);
        Assert.That(frame.Name, Does.Contain("Main"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:afterSingle")));
    }

    // Stepping out of the inner Dispose of two stacked usings crosses the inner finally, the plumbing
    // between the two handlers (which belongs to neither) and the outer finally in one step
    [Test]
    public void StepOutOfTheInnerDisposeCrossesBothFinallys() {
        Launch();
        // The third Dispose call is the inner resource of the nested pair (after 'single' and 'braced')
        SetBreakpoints(new SourceBreakpoint() { Line = GetMarkerLine("marker:dispose"), HitCondition = "3" });
        ConfigurationDone();
        var threadId = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint).ThreadId!.Value;
        Assert.That(Evaluate("Id", threadId).Result, Is.EqualTo("4"));

        var frame = StepOut(threadId);
        Assert.That(frame.Name, Does.Contain("Main"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:afterNested")));
    }

    // A step into at the closing brace enters the Dispose call the hidden finally makes - the resumed
    // step keeps the user's kind rather than falling back to a step over
    [Test]
    public void StepIntoAtTheClosingBraceEntersDispose() {
        var threadId = LaunchToMarker("marker:bracedClose");
        var frame = StepIn(threadId);
        Assert.That(frame.Name, Does.Contain("Dispose"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:dispose")));
    }

    // A finally the user wrote has sequence points with real lines, the step must stop on it, not treat
    // it like a compiler generated region and skip it
    [Test]
    public void StepOverDoesNotSkipAUserWrittenFinally() {
        var threadId = LaunchToMarker("marker:tryBody");
        var lines = new List<int>();
        var afterFinallyLine = GetMarkerLine("marker:afterFinally");
        for (var i = 0; i < 6 && (lines.Count == 0 || lines[^1] != afterFinallyLine); i++)
            lines.Add(StepOver(threadId).Line);
        Assert.That(lines, Does.Contain(GetMarkerLine("marker:finallyBody")));
        Assert.That(lines[^1], Is.EqualTo(afterFinallyLine));
    }

    // A using at the end of a method puts the hidden finally between the last statement and the closing
    // brace. The step passes through it onto the brace, and the step after leaves the method
    [Test]
    public void StepOverUsingBodyAtTheEndOfAMethodReachesTheClosingBraceThenTheCaller() {
        var threadId = LaunchToMarker("marker:tailBody");
        var closing = StepOver(threadId);
        Assert.That(closing.Line, Is.EqualTo(GetMarkerLine("marker:tailClose")));

        var caller = StepOver(threadId);
        Assert.That(caller.Name, Does.Contain("Main"));
        Assert.That(caller.Line, Is.EqualTo(GetMarkerLine("marker:tailCall")));
    }

    // A try with catch blocks and a finally nests the try/catch inside a try/finally. The leave out of the
    // outer try, past the catch blocks, is hidden code that maps back to the last catch statement: the step
    // over the closing brace of the try used to stop on the 'throw;' of a catch that never ran
    [Test]
    public void StepOverTryCloseWithCatchAndFinallyReachesTheFinally() {
        var threadId = LaunchToMarker("marker:catchFinallyBody");
        var closing = StepOver(threadId);
        Assert.That(closing.Line, Is.EqualTo(GetMarkerLine("marker:catchFinallyClose")));

        var opening = StepOver(threadId);
        Assert.That(opening.Line, Is.EqualTo(GetMarkerLine("marker:catchFinallyOpen")));
        Assert.That(opening.Line, Is.Not.EqualTo(GetMarkerLine("marker:rethrow")));

        var cleanup = StepOver(threadId);
        Assert.That(cleanup.Line, Is.EqualTo(GetMarkerLine("marker:catchFinallyCleanup")));
    }

    [Test]
    public void StepIntoTryCloseWithCatchAndFinallyReachesTheFinally() {
        var threadId = LaunchToMarker("marker:catchFinallyClose");
        var opening = StepIn(threadId);
        Assert.That(opening.Line, Is.EqualTo(GetMarkerLine("marker:catchFinallyOpen")));
    }

    // A failed 'when' clause jumps into the hidden dispatch of the switch that binds the next case's
    // pattern variable; that code maps back to the 'break' of the case just failed
    [Test]
    public void StepOverFailingWhenClauseReachesTheNextCase() {
        var threadId = LaunchToMarker("marker:negativeGuard");
        var secondGuard = StepOver(threadId);
        Assert.That(secondGuard.Line, Is.EqualTo(GetMarkerLine("marker:bigGuard")));

        var body = StepOver(threadId);
        Assert.That(body.Line, Is.EqualTo(GetMarkerLine("marker:otherBody")));
        Assert.That(StepOver(threadId).Line, Is.EqualTo(GetMarkerLine("marker:otherBreak")));

        var after = StepOver(threadId);
        Assert.That(after.Line, Is.EqualTo(GetMarkerLine("marker:afterSwitch")));
    }
}
