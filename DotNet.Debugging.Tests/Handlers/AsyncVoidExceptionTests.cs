using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class AsyncVoidExceptionTests : BaseDebugTestFixture {
    public AsyncVoidExceptionTests() : base(nameof(AsyncVoidExceptionTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        Worker.Fire();
        await Task.Delay(5000); // the crash ends the program long before

        public static class Worker {
            public static async void Fire() {
                await Task.Yield();
                throw new InvalidOperationException("async void boom"); // marker:throw
            }
        }
        """;
    }

    // The compiler records the catch handler of an async void method in the PDB the way it does async Main's: the
    // builder rethrows what that catch takes on the synchronization context, where no user code can handle it. So
    // the stop is 'user-unhandled' at the catch, with the throwing frame still live; the rethrow then ends the process
    [Test]
    public void AsyncVoidThrowIsUserUnhandledTest() {
        LaunchWithExceptionFilters("user-unhandled");

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(stopped.Text, Is.EqualTo($"An exception of type 'System.InvalidOperationException' occurred in {ProjectName}.dll but was not handled in user code"));
        Assert.That(GetExceptionInfo(stopped.ThreadId!.Value).BreakMode, Is.EqualTo(ExceptionBreakMode.UserUnhandled));
        var top = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(top.Name, Does.Contain("Worker.<Fire>").And.Not.StartWith("[Exception]"), "The throwing frame is still live at the state machine's catch");
        Assert.That(top.Line, Is.EqualTo(GetMarkerLine("marker:throw")));
        Assert.That(GetLocalVariables(stopped.ThreadId!.Value).Select(it => it.Name), Has.Some.StartsWith("$exception"));

        Continue(stopped.ThreadId!.Value);
        var unhandled = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(unhandled.Text, Does.StartWith("An unhandled exception of type 'System.InvalidOperationException'"));
        Assert.That(GetExceptionInfo(unhandled.ThreadId!.Value).BreakMode, Is.EqualTo(ExceptionBreakMode.Unhandled));
        var frames = GetStackFrames(unhandled.ThreadId!.Value);
        Assert.That(frames[0].Name, Does.StartWith("[Exception] ").And.Contain("Worker.<Fire>"), "The rethrow happens on another thread; the throw site comes from the recorded trace");
        Assert.That(frames[0].Line, Is.EqualTo(GetMarkerLine("marker:throw")));

        Continue(unhandled.ThreadId!.Value);
        WaitForEvent<TerminatedEvent>();
    }
}
