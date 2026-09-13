using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A step into a [DebuggerStepThrough], [DebuggerNonUserCode] or [DebuggerHidden] method that calls no user code
// lands on the next statement of the caller. Without Just My Code only [DebuggerNonUserCode] is entered
public class AttributedMethodStepInTests : BaseDebugTestFixture {
    public AttributedMethodStepInTests() : base(nameof(AttributedMethodStepInTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        Skipped(); // marker:first
        Library(); // marker:second
        Concealed(); // marker:third
        Console.WriteLine("end"); // marker:end

        [System.Diagnostics.DebuggerStepThrough] static void Skipped() { Console.WriteLine("skipped"); }
        [System.Diagnostics.DebuggerNonUserCode] static void Library() { Console.WriteLine("library"); } // marker:insideLibrary
        [System.Diagnostics.DebuggerHidden] static void Concealed() { Console.WriteLine("concealed"); }
        """;
    }

    [Test]
    public void StepIntoAttributedMethodsIsSteppedOverTest() {
        var threadId = LaunchToMarker("marker:first");

        Assert.That(StepIn(threadId).Line, Is.EqualTo(GetMarkerLine("marker:second")), "[DebuggerStepThrough]");
        Assert.That(StepIn(threadId).Line, Is.EqualTo(GetMarkerLine("marker:third")), "[DebuggerNonUserCode]");
        Assert.That(StepIn(threadId).Line, Is.EqualTo(GetMarkerLine("marker:end")), "[DebuggerHidden]");
    }

    [Test]
    public void StepIntoNonUserCodeMethodWithoutJustMyCodeTest() {
        var threadId = LaunchToMarker("marker:first", justMyCode: false);

        Assert.That(StepIn(threadId).Line, Is.EqualTo(GetMarkerLine("marker:second")), "[DebuggerStepThrough] is honored without Just My Code");
        var inside = StepIn(threadId);
        Assert.That(inside.Line, Is.EqualTo(GetMarkerLine("marker:insideLibrary")), "[DebuggerNonUserCode] is entered without Just My Code");
        Assert.That(inside.Name, Does.Contain("Library"));
        Assert.That(StepOut(threadId).Line, Is.EqualTo(GetMarkerLine("marker:second")));
        Assert.That(StepIn(threadId).Line, Is.EqualTo(GetMarkerLine("marker:third")));
        Assert.That(StepIn(threadId).Line, Is.EqualTo(GetMarkerLine("marker:end")), "[DebuggerHidden] is honored without Just My Code");
    }
}
