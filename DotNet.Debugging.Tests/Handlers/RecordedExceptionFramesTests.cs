using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class RecordedExceptionFramesTests : BaseDebugTestFixture {
    public RecordedExceptionFramesTests() : base(nameof(RecordedExceptionFramesTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        await Worker.RunAsync(); // marker:await

        public static class Worker {
            public static async Task RunAsync() {
                await Task.Yield();
                Helper.Fail(); // marker:call
            }
        }
        public static class Helper {
            public static void Fail() {
                throw new InvalidOperationException("async boom"); // marker:throw
            }
        }
        """;
    }

    // RunAsync's state machine catches the throw - a handler the awaiting code may still see the exception through,
    // so no stop - and the task's exception is rethrown at Main's await, where async Main's own catch takes it:
    // 'user-unhandled'. The frames of the raise are gone by then; the stack lists them from the exception's recorded
    // trace, above the live frames of the rethrow, and they open the source like any other frame
    [Test]
    public void RecordedFramesAboveLiveFramesTest() {
        LaunchWithExceptionFilters("user-unhandled");

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(stopped.Text, Does.EndWith("but was not handled in user code"));
        Assert.That(GetExceptionInfo(stopped.ThreadId!.Value).BreakMode, Is.EqualTo(ExceptionBreakMode.UserUnhandled));

        var frames = GetStackFrames(stopped.ThreadId!.Value);
        Assert.That(frames[0].Name, Does.StartWith("[Exception] ").And.Contain("Helper.Fail"));
        Assert.That(frames[0].Line, Is.EqualTo(GetMarkerLine("marker:throw")));
        Assert.That(frames[0].Source?.Path, Is.EqualTo(ProgramFilePath));
        Assert.That(frames[1].Name, Does.StartWith("[Exception] ").And.Contain("Worker.<RunAsync>"), "The state machine frame is qualified with its enclosing type");
        Assert.That(frames[1].Line, Is.EqualTo(GetMarkerLine("marker:call")));
        Assert.That(frames.Skip(2).Select(it => it.Name), Has.None.StartsWith("[Exception]"), "Only the unwound frames come from the recorded trace");
        var main = frames.Skip(2).FirstOrDefault(it => it.Line == GetMarkerLine("marker:await"));
        Assert.That(main, Is.Not.Null, $"The live frame of the await is listed: {string.Join(" | ", frames.Select(it => it.Name))}");
        Assert.That(main!.PresentationHint, Is.Null, "The await sits in hidden code, but the frame is charged to the await's line like any user frame");
        // RunAsync's state machine is still live too, completing its task from inside the compiler's catch: no statement
        // of the method runs there, so the frame has no line and is de-emphasized like the runtime's frames around it
        var inCatch = frames.Skip(2).FirstOrDefault(it => it.Name.Contains("Worker.<RunAsync>"));
        Assert.That(inCatch, Is.Not.Null, $"The state machine completing its task is on the stack: {string.Join(" | ", frames.Select(it => it.Name))}");
        Assert.That(inCatch!.Line, Is.EqualTo(0));
        Assert.That(inCatch.Source, Is.Null);
        Assert.That(inCatch.PresentationHint, Is.EqualTo(StackFrame.PresentationHintValue.Subtle));
        Assert.That(frames.Where(it => it.Name.StartsWith("System.Private.CoreLib.dll!")).Select(it => it.PresentationHint), Is.Not.Empty.And.All.EqualTo(StackFrame.PresentationHintValue.Subtle));
        Assert.That(frames[0].PresentationHint, Is.Null, "A recorded user frame is a user frame");
        var recordedScopes = Host.SendRequestSync(new ScopesRequest() { FrameId = frames[0].Id }).Scopes;
        Assert.That(recordedScopes, Has.Count.EqualTo(1), "A recorded frame shows the exception");
        Assert.That(GetVariables(recordedScopes[0].VariablesReference).Select(it => it.Name), Is.EqualTo(new[] { "$exception [InvalidOperationException]" }), "...and nothing else, its locals went with the frame");
        Assert.That(Evaluate("$exception.Message", stopped.ThreadId!.Value).Result, Is.EqualTo("\"async boom\""), "The exception evaluates in the recorded frame");
        Assert.That(Host.SendRequestSync(new ScopesRequest() { FrameId = main.Id }).Scopes, Is.Not.Empty, "A live frame keeps its locals");

        Continue(stopped.ThreadId!.Value);
        var unhandled = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(GetExceptionInfo(unhandled.ThreadId!.Value).BreakMode, Is.EqualTo(ExceptionBreakMode.Unhandled));
        frames = GetStackFrames(unhandled.ThreadId!.Value);
        Assert.That(frames[0].Name, Does.StartWith("[Exception] ").And.Contain("Helper.Fail"));
        Assert.That(frames.Select(it => it.Name), Has.Some.StartsWith("[Exception] ").And.Contain("<Main>$"), "Main's own frame is recorded by now");
        Assert.That(frames.Last().Name, Does.Contain(".<Main>(").And.Not.StartWith("[Exception]"));

        Continue(unhandled.ThreadId!.Value);
        WaitForEvent<TerminatedEvent>();
    }

    // Break-on-all stops at the raise itself, where every frame of the dispatch is live and nothing is listed twice;
    // the rethrow at Main's await is the next stop, with the unwound frames recorded above the live ones
    [Test]
    public void LiveFramesAreNotRepeatedTest() {
        LaunchWithExceptionFilters("all");

        var raise = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        var frames = GetStackFrames(raise.ThreadId!.Value);
        Assert.That(frames[0].Name, Does.Contain("Helper.Fail").And.Not.StartWith("[Exception]"));
        Assert.That(frames.Select(it => it.Name), Has.None.StartsWith("[Exception]"));

        Continue(raise.ThreadId!.Value);
        var rethrow = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        frames = GetStackFrames(rethrow.ThreadId!.Value);
        Assert.That(frames[0].Name, Does.StartWith("[Exception] ").And.Contain("Helper.Fail"));
        Assert.That(frames[1].Name, Does.StartWith("[Exception] ").And.Contain("RunAsync"));
        Assert.That(frames.Count(it => it.Name.Contains("Helper.Fail")), Is.EqualTo(1));

        Continue(rethrow.ThreadId!.Value);
        var remaining = CollectStopsUntilExit();
        Assert.That(remaining.Select(it => it.Text), Is.EqualTo(new[] {
            $"An unhandled exception of type 'System.InvalidOperationException' occurred in {ProjectName}.dll",
        }), "The escaping catch's report stays hidden without the user-unhandled filter");
    }
}
