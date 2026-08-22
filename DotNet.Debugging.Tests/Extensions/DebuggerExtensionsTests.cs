using DotNet.Debugging.Adapter.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Models;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;
using ExceptionFilterOptions = DotNet.Debugging.Adapter.ExceptionFilterOptions;

namespace DotNet.Debugging.Tests;

public class DebuggerExtensionsTests {

    [TestCase("counter", "int", "counter [int]")]
    [TestCase("value", "double?", "value [double?]")]
    [TestCase("message", "string", "message [string]")]
    [TestCase("items", "string[]", "items [string[]]")]
    [TestCase("grid", "int[,]", "grid [int[,]]")]
    [TestCase("item", "SampleClass", "item [SampleClass]")]
    [TestCase("item", "SimpleConsole.Class2", "item [Class2]")]
    [TestCase("item", "My.Namespace.Program.MyType", "item [MyType]")]
    [TestCase("items", "My.Namespace.MyType[]", "items [MyType[]]")]
    [TestCase("map", "System.Collections.Generic.Dictionary<string, My.Ns.Foo[]>", "map [Dictionary<string, Foo[]>]")]
    [TestCase("nested", "System.Collections.Generic.List<System.Collections.Generic.List<int>>", "nested [List<List<int>>]")]
    [TestCase("[0]", "int", "[0] [int]")]
    [TestCase("Static members", "", "Static members")]
    [TestCase("[More]", null, "[More]")]
    public void ToDisplayNameTest(string name, string? type, string expected) {
        Assert.That(name.ToDisplayName(type), Is.EqualTo(expected));
    }

    [TestCase("counter [int]", "counter")]
    [TestCase("value [double?]", "value")]
    [TestCase("[30] [int]", "[30]")]
    [TestCase("counter", "counter")]
    [TestCase("items [string[]]", "items")]
    [TestCase("item [SampleClass]", "item")]
    [TestCase("map [Dictionary<string, Foo[]>]", "map")]
    [TestCase("[\"key\"] [string]", "[\"key\"]")]
    [TestCase("[0]", "[0]")]
    public void ToVariableNameTest(string displayName, string expected) {
        Assert.That(displayName.ToVariableName(), Is.EqualTo(expected));
    }

    [TestCase(StopReason.Breakpoint, StoppedEvent.ReasonValue.Breakpoint)]
    [TestCase(StopReason.Step, StoppedEvent.ReasonValue.Step)]
    [TestCase(StopReason.Pause, StoppedEvent.ReasonValue.Pause)]
    [TestCase(StopReason.Entry, StoppedEvent.ReasonValue.Entry)]
    public void ToStoppedReasonTest(StopReason reason, StoppedEvent.ReasonValue expected) {
        Assert.That(reason.ToStoppedReason(), Is.EqualTo(expected));
    }

    [TestCase(null, false, "<No Name>")]
    [TestCase("", false, "<No Name>")]
    [TestCase(null, true, "Main Thread")]
    [TestCase("My Worker", true, "My Worker")]
    public void ToThreadNameTest(string? threadName, bool isMain, string expected) {
        Assert.That(new ThreadInfo(1, threadName, isMain).ToDisplayName(), Is.EqualTo(expected));
    }

    [TestCase(StackFrameKind.Managed, "Program.Main(string[] args)", "App.dll", 7, "App.dll!Program.Main(string[] args) Line 7")]
    [TestCase(StackFrameKind.Managed, "Program.Main(string[] args)", "App.dll", null, "App.dll!Program.Main(string[] args)")]
    [TestCase(StackFrameKind.Native, "[Native Frame]", null, null, "[Native Frame]")]
    public void ToFrameDisplayNameTest(StackFrameKind kind, string name, string? moduleName, int? line, string expected) {
        var frame = new StackFrameInfo(1, kind, name);
        frame.ModuleName = moduleName;
        if (line != null)
            frame.Location = new SourceLocation("Program.cs", line.Value, 1, line.Value, 10);
        Assert.That(frame.ToDisplayName(), Is.EqualTo(expected));
    }

    [TestCase(1, 0, 0, 0, "1.00.0.0")]
    [TestCase(10, 0, 1126, 37416, "10.00.1126.37416")]
    [TestCase(2, 5, -1, -1, "2.05.0.0")]
    public void ToDisplayVersionTest(int major, int minor, int build, int revision, string expected) {
        var version = build < 0 ? new Version(major, minor) : new Version(major, minor, build, revision);
        Assert.That(version.ToDisplayVersion(), Is.EqualTo(expected));
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