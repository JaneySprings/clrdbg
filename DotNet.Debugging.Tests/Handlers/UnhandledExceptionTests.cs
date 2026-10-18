using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class UnhandledExceptionTests : BaseDebugTestFixture {
    public UnhandledExceptionTests() : base(nameof(UnhandledExceptionTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        await Task.Yield();
        Worker.Run();

        public static class Worker {
            public static void Run() {
                ProcessRequest();
            }
            private static void ProcessRequest() {
                throw new InvalidOperationException("unhandled boom"); // marker:throw
            }
        }
        """;
    }

    // The exception is caught by async Main's builder catch. The compiler records that handler in the PDB because
    // the synthesized '<Main>' is bound to rethrow the task's exception where nothing handles it, so the stop is
    // 'user-unhandled' right there, while the throwing frames are still on the stack. The 'unhandled' stop follows
    // at the bridge rethrow and fires regardless of the filters; by then the frames of the raise are gone and the
    // stack lists them from the exception's recorded trace, above the live bridge frame
    [Test]
    public void UserUnhandledThenUnhandledStopTest() {
        LaunchWithExceptionFilters("user-unhandled");

        var first = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(first.Text, Is.EqualTo($"An exception of type 'System.InvalidOperationException' occurred in {ProjectName}.dll but was not handled in user code"));
        Assert.That(GetExceptionInfo(first.ThreadId!.Value).BreakMode, Is.EqualTo(ExceptionBreakMode.UserUnhandled));
        var top = GetTopStackFrame(first.ThreadId!.Value);
        Assert.That(top.Name, Does.Contain("Worker.ProcessRequest").And.Not.StartWith("[Exception]"), "The throwing frame is still live at the state machine's catch");
        Assert.That(top.Line, Is.EqualTo(GetMarkerLine("marker:throw")));

        Continue(first.ThreadId!.Value);
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(stopped.Text, Is.EqualTo($"An unhandled exception of type 'System.InvalidOperationException' occurred in {ProjectName}.dll"),
            "The 'unhandled' stop names the user's module: the last rethrow happens in the core library's await machinery, which is hidden from stack traces");
        var info = GetExceptionInfo(stopped.ThreadId!.Value);
        Assert.That(info.BreakMode, Is.EqualTo(ExceptionBreakMode.Unhandled));
        Assert.That(info.Details?.StackTrace, Does.Contain("Worker.ProcessRequest()"));
        Assert.That(info.Details?.StackTrace, Does.Contain(".<Main>("), "The recorded trace reaches through the bridge rethrow");

        var frames = GetStackFrames(stopped.ThreadId!.Value);
        Assert.That(frames[0].Name, Does.StartWith("[Exception] ").And.Contain("Worker.ProcessRequest"), "The throw site heads the list although its frame is gone");
        Assert.That(frames[0].Line, Is.EqualTo(GetMarkerLine("marker:throw")));
        Assert.That(frames[0].Source?.Path, Is.EqualTo(ProgramFilePath));
        Assert.That(frames[1].Name, Does.StartWith("[Exception] ").And.Contain("Worker.Run"));
        Assert.That(frames.Last().Name, Does.Contain(".<Main>(").And.Not.StartWith("[Exception]"), "The live bridge frame closes the list");
        Assert.That(GetLocalVariables(stopped.ThreadId!.Value).Select(it => it.Name), Is.EqualTo(new[] { "$exception [InvalidOperationException]" }), "A recorded frame shows the exception and nothing else");

        Continue(stopped.ThreadId!.Value);
        WaitForEvent<TerminatedEvent>();
    }

    // Under break-on-all the same crash yields exactly two stops: the first chance at the throw and the
    // final 'unhandled' one. The bridge rethrow adds no stop - '<Main>' has no sequence points, so it is
    // not user code - and the 'user-unhandled' report stays hidden without its filter
    [Test]
    public void BreakOnAllCrashStopsTest() {
        LaunchWithExceptionFilters("all");

        var stops = CollectStopsUntilExit();

        Assert.That(stops.Select(it => it.Text), Is.EqualTo(new[] {
            $"Exception thrown: 'System.InvalidOperationException' in {ProjectName}.dll",
            $"An unhandled exception of type 'System.InvalidOperationException' occurred in {ProjectName}.dll",
        }));
    }
}
