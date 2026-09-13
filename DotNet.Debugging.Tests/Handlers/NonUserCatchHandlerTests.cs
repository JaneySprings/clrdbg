using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// An exception thrown in user code and swallowed by a method the user's own assembly marks as non-user code
// ([DebuggerNonUserCode], [DebuggerStepThrough]) left the code the user is debugging: it is 'user-unhandled' under
// Just My Code, whichever module the handler lives in
public class NonUserCatchHandlerTests : BaseDebugTestFixture {
    public NonUserCatchHandlerTests() : base(nameof(NonUserCatchHandlerTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        QuietRunner.Invoke(() => throw new InvalidOperationException("raised by the callback")); // marker:nonUserCode
        SkippedRunner.Invoke(() => throw new FormatException("raised again")); // marker:stepThrough
        PlainRunner.Invoke(() => throw new ArgumentException("swallowed by user code")); // marker:userCode
        Console.WriteLine("end"); // marker:end

        [System.Diagnostics.DebuggerNonUserCode]
        static class QuietRunner {
            public static void Invoke(Action callback) {
                try { callback(); } catch { }
            }
        }
        [System.Diagnostics.DebuggerStepThrough]
        static class SkippedRunner {
            public static void Invoke(Action callback) {
                try { callback(); } catch { }
            }
        }
        static class PlainRunner {
            public static void Invoke(Action callback) {
                try { callback(); } catch { }
            }
        }
        """;
    }

    [Test]
    public void CatchInAttributedMethodOfUserAssemblyIsUserUnhandledTest() {
        Launch();
        SetBreakpoints(GetMarkerLine("marker:end"));
        SetExceptionBreakpoints(["user-unhandled"], ("user-unhandled", null));
        ConfigurationDone();

        var first = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(first.Text, Is.EqualTo($"An exception of type 'System.InvalidOperationException' occurred in {ProjectName}.dll but was not handled in user code"));
        Assert.That(GetExceptionInfo(first.ThreadId!.Value).BreakMode, Is.EqualTo(ExceptionBreakMode.UserUnhandled));
        Assert.That(GetTopStackFrame(first.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:nonUserCode")), "The throwing user frame is on top");
        Continue(first.ThreadId!.Value);

        var second = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(second.Text, Does.StartWith("An exception of type 'System.FormatException'"), "A [DebuggerStepThrough] handler is non-user code as well");
        Continue(second.ThreadId!.Value);

        var end = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(end.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:end")), "An exception caught by plain user code does not stop");
        Continue(end.ThreadId!.Value);
        Assert.That(CollectStopsUntilExit(), Is.Empty);
    }
}
