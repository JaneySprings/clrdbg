using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// Byref-like values ('Span<T>' and friends) must never travel through a func eval - the debuggee can
// die on it. Their display is the plain type and their expansion lists fields only
public class ByRefLikeVariableTests : BaseDebugTestFixture {
    public ByRefLikeVariableTests() : base(nameof(ByRefLikeVariableTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var text = "byref";
        var span = text.AsSpan();
        var length = span.Length; // marker:stop
        Console.WriteLine(length);
        """;
    }

    [Test]
    public void SpanIsShownWithoutFuncEvalsTest() {
        var threadId = LaunchToMarker();
        var span = GetLocalVariables(threadId).Single(it => it.Name.StartsWith("span"));
        Assert.That(span.Value, Does.StartWith("{").And.Contain("ReadOnlySpan"));

        // The expansion runs no getters: fields only, however deep the groups go
        Assert.That(span.VariablesReference, Is.GreaterThan(0));
        var members = GetVariables(span.VariablesReference);
        var nonPublicGroup = members.FirstOrDefault(it => it.Name == "Non-Public members");
        if (nonPublicGroup != null)
            members.AddRange(GetVariables(nonPublicGroup.VariablesReference));
        Assert.That(members.Select(it => it.Name), Has.Some.StartsWith("_length"));
        Assert.That(members.Select(it => it.Name), Has.None.StartsWith("Length"));

        // The debuggee survived every variable request above
        Continue(threadId);
        WaitForEvent<ExitedEvent>();
        Assert.That(WaitForEvent<TerminatedEvent>(), Is.Not.Null);
    }
}
