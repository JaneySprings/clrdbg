using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// An exception thrown by a method that was invoked through reflection leaves user code into the runtime's invoker,
// which catches it to wrap it. On Unix the dispatch crosses a native frame on the way and the runtime raises the
// exception again in the managed caller beyond it: that raise continues the first one. It is how a framework calls
// into an app (a MAUI page constructor runs this way), so the stop has to blame the app, not the core library
public class ReflectionInvokeExceptionTests : BaseDebugTestFixture {
    public ReflectionInvokeExceptionTests() : base(nameof(ReflectionInvokeExceptionTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        using System.Reflection;

        try {
            typeof(Target).GetMethod("Boom")!.Invoke(null, null);
        } catch (TargetInvocationException ex) {
            Console.WriteLine($"caught: {ex.InnerException?.GetType().Name}");
        }
        Console.WriteLine("done");

        public static class Target {
            public static void Boom() {
                throw new InvalidOperationException("reflected boom"); // marker:throw
            }
        }
        """;
    }

    [Test]
    public void ExceptionCaughtByTheInvokerIsUserUnhandledInTheUserModuleTest() {
        LaunchWithExceptionFilters("user-unhandled");

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(stopped.Text, Is.EqualTo($"An exception of type 'System.InvalidOperationException' occurred in {ProjectName}.dll but was not handled in user code"),
            "The raise the runtime repeats beyond the native invoke frame does not move the blame to the core library");

        var info = GetExceptionInfo(stopped.ThreadId!.Value);
        Assert.That(info.ExceptionId, Is.EqualTo("System.InvalidOperationException"));
        Assert.That(info.BreakMode, Is.EqualTo(ExceptionBreakMode.UserUnhandled));
        Assert.That(info.Details?.Message, Is.EqualTo("reflected boom"));
        // The throwing frame may be unwound by the time of the stop (it is, where the dispatch crossed a native
        // frame), the recorded trace still begins at it
        Assert.That(info.Details?.StackTrace, Does.StartWith("   at Target.Boom() in "));
        Assert.That(info.Details?.StackTrace, Does.Contain($":line {GetMarkerLine("marker:throw")}"));

        Continue(stopped.ThreadId!.Value);
        Assert.That(CollectStopsUntilExit(), Is.Empty, "The wrapper exception is caught by user code and stops nothing");
    }

    [Test]
    public void FirstChanceStopsNameTheModuleOfEachRaiseTest() {
        LaunchWithExceptionFilters("all");

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(stopped.Text, Is.EqualTo($"Exception thrown: 'System.InvalidOperationException' in {ProjectName}.dll"));
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:throw")));
        Continue(stopped.ThreadId!.Value);

        // The wrapper is a raise of its own, made by the core library: the kept module of the first raise does not leak into it
        var texts = new List<string>();
        CollectStopsUntilExit(it => texts.Add(it.Text));
        Assert.That(texts, Has.Some.EqualTo("Exception thrown: 'System.Reflection.TargetInvocationException' in System.Private.CoreLib.dll"));
        Assert.That(texts, Has.None.Contains($"'System.Reflection.TargetInvocationException' in {ProjectName}.dll"));
    }
}
