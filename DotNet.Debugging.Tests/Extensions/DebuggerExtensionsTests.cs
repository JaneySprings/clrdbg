using DotNet.Debugging.Adapter.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;
using ExceptionFilterOptions = DotNet.Debugging.Adapter.ExceptionFilterOptions;

namespace DotNet.Debugging.Tests;

public class DebuggerExtensionsTests {

    [TestCase("counter", "int", "counter [int]")]
    [TestCase("value", "double?", "value [double?]")]
    [TestCase("message", "string", "message [string]")]
    [TestCase("items", "string[]", "items")]
    [TestCase("item", "SampleClass", "item")]
    public void ToDisplayNameTest(string name, string type, string expected) {
        Assert.That(name.ToDisplayName(type), Is.EqualTo(expected));
    }

    [TestCase("counter [int]", "counter")]
    [TestCase("value [double?]", "value")]
    [TestCase("[30] [int]", "[30]")]
    [TestCase("counter", "counter")]
    [TestCase("item [SampleClass]", "item [SampleClass]")]
    public void ToVariableNameTest(string displayName, string expected) {
        Assert.That(displayName.ToVariableName(), Is.EqualTo(expected));
    }

    [TestCase("breakpoint", StoppedEvent.ReasonValue.Breakpoint)]
    [TestCase("step", StoppedEvent.ReasonValue.Step)]
    [TestCase("exception", StoppedEvent.ReasonValue.Exception)]
    [TestCase("entry", StoppedEvent.ReasonValue.Entry)]
    [TestCase("goto", StoppedEvent.ReasonValue.Goto)]
    [TestCase("something else", StoppedEvent.ReasonValue.Unknown)]
    public void ToStoppedReasonTest(string reason, StoppedEvent.ReasonValue expected) {
        Assert.That(reason.ToStoppedReason(), Is.EqualTo(expected));
    }

    [TestCase(null, "<No Name>")]
    [TestCase("", "<No Name>")]
    [TestCase("My Worker", "My Worker")]
    public void ToThreadNameTest(string? threadName, string expected) {
        Assert.That(threadName.ToThreadName(1), Is.EqualTo(expected));
    }

    [Test]
    public void ExceptionFilterDisabledTest() {
        var filter = new ExceptionFilterOptions();
        Assert.That(filter.ShouldStopOnException("System.Exception"), Is.False);
    }

    [Test]
    public void ExceptionFilterEnabledTest() {
        var filter = new ExceptionFilterOptions();
        filter.Enable();
        Assert.That(filter.ShouldStopOnException("System.Exception"), Is.True);
        Assert.That(filter.ShouldStopOnException(null), Is.True);
    }

    [Test]
    public void ExceptionFilterIncludeConditionTest() {
        var filter = new ExceptionFilterOptions();
        filter.Enable("System.InvalidOperationException, System.ArgumentException");
        Assert.That(filter.ShouldStopOnException("System.InvalidOperationException"), Is.True);
        Assert.That(filter.ShouldStopOnException("System.ArgumentException"), Is.True);
        Assert.That(filter.ShouldStopOnException("System.Exception"), Is.False);
    }

    [Test]
    public void ExceptionFilterExcludeConditionTest() {
        var filter = new ExceptionFilterOptions();
        filter.Enable("!System.InvalidOperationException");
        Assert.That(filter.ShouldStopOnException("System.InvalidOperationException"), Is.False);
        Assert.That(filter.ShouldStopOnException("System.Exception"), Is.True);
    }

    [Test]
    public void ExceptionFilterResetTest() {
        var filter = new ExceptionFilterOptions();
        filter.Enable("!System.InvalidOperationException");
        filter.Reset();
        Assert.That(filter.Enabled, Is.False);
        filter.Enable();
        Assert.That(filter.ShouldStopOnException("System.InvalidOperationException"), Is.True, "Reset must clear the ignore list");
    }
}