using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class ExceptionStackTraceTests : BaseDebugTestFixture {
    public ExceptionStackTraceTests() : base(nameof(ExceptionStackTraceTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        try {
            await ExceptionRelay.OuterAsync();
        } catch (Exception ex) {
            Console.WriteLine($"caught: {ex.GetType().Name}");
        }
        Console.WriteLine("done");

        public static class ExceptionRelay {
            public static async Task OuterAsync() {
                await MiddleAsync();
            }
            public static async Task MiddleAsync() {
                try {
                    await InnerAsync();
                } catch (Exception e) {
                    Rethrow(e);
                }
            }
            public static async Task InnerAsync() {
                await Task.CompletedTask;
                throw new InvalidOperationException("relay boom");
            }
            private static void Rethrow(Exception e) {
                throw e;
            }
        }
        """;
    }

    [Test]
    public void RecordedStackTraceAcrossAsyncRethrowTest() {
        LaunchWithExceptionFilters("all");

        // Break-on-all stops on every dispatch of the exception entering user code - collect the recorded
        // stack trace reported at each stop until the debuggee runs to completion
        var stackTraces = new List<string>();
        CollectStopsUntilExit(stopped => stackTraces.Add(GetExceptionInfo(stopped.ThreadId!.Value).Details?.StackTrace ?? string.Empty));

        Assert.That(stackTraces, Is.Not.Empty, "Break-on-all must stop at the exception");
        Assert.That(stackTraces[0], Does.Contain("ExceptionRelay.<InnerAsync>"), "The first stop shows the throw site inside the state machine");
        Assert.That(stackTraces[0], Does.Contain(":line "), "The throw site carries its source line");

        // 'throw e' in Rethrow resets the recorded trace, which then grows through the async hops - the
        // 'MoveNext' frames of dispatches that already completed, which no walk of the thread's stack can see
        var rethrown = stackTraces.LastOrDefault(it => it.Contains("ExceptionRelay.Rethrow(System.Exception e)") && it.Contains("<MiddleAsync>"));
        Assert.That(rethrown, Is.Not.Null, $"No stop reported the rethrown trace. Reported traces:\n{string.Join("\n---\n", stackTraces)}");
        Assert.That(rethrown, Does.StartWith("   at ExceptionRelay.Rethrow(System.Exception e)"), "The rethrow site is the most recent recorded frame");
        Assert.That(rethrown, Does.Not.Contain("InnerAsync"), "The rethrow reset the previously recorded frames");
        Assert.That(rethrown, Does.Not.Contain("ExceptionDispatchInfo"), "The [StackTraceHidden] machinery of the await hops is dropped");
        Assert.That(rethrown, Does.Not.Contain("TaskAwaiter"));
    }

    [Test]
    public void ModuleAttributionAcrossAsyncHopsTest() {
        LaunchWithExceptionFilters("all");

        var moduleNames = CollectStopsUntilExit().Select(it => it.Text!.Split(" in ").Last()).ToList();

        // The stop names the user's module at each dispatch: the throw and the 'throw e' rethrow happen in user
        // code, and the hops in between are rethrown by the core library's await machinery, which is hidden from
        // stack traces and charged to the user's async method beneath it
        Assert.That(moduleNames, Is.EqualTo(new[] {
            $"{ProjectName}.dll",
            $"{ProjectName}.dll",
            $"{ProjectName}.dll",
            $"{ProjectName}.dll",
            $"{ProjectName}.dll",
        }));
    }

    [Test]
    public void UserUnhandledSkipsAsyncRelayTest() {
        LaunchWithExceptionFilters("user-unhandled");

        // Every catch on the way is user code: the explicit handlers and the async state machines' own
        // catch blocks compiled into 'MoveNext', so there is no user-unhandled stop for this program
        var stops = CollectStopsUntilExit();
        Assert.That(stops.Where(it => it.Reason == StoppedEvent.ReasonValue.Exception), Is.Empty,
            $"An exception caught inside user code (including a state machine's catch) must not stop: {string.Join(" | ", stops.Select(it => it.Text))}");
    }
}
