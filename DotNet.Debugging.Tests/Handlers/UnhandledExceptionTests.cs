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
                throw new InvalidOperationException("unhandled boom");
            }
        }
        """;
    }

    // The exception is caught by async Main's builder catch (user code, so no user-unhandled stop),
    // stored into the main task and rethrown by the compiler's '<Main>' bridge, where nothing handles it -
    // the 'unhandled' stop fires regardless of the filters
    [Test]
    public void UnhandledStopTest() {
        Launch();
        SetExceptionBreakpoints(new[] { "user-unhandled" }, ("user-unhandled", null));
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(stopped.Text, Is.EqualTo("An unhandled exception of type 'System.InvalidOperationException' occurred in System.Private.CoreLib.dll"),
            "The 'unhandled' stop names the module of the last rethrow - the core library's await machinery");
        var info = Host.SendRequestSync(new ExceptionInfoRequest() { ThreadId = stopped.ThreadId!.Value });
        Assert.That(info.BreakMode, Is.EqualTo(ExceptionBreakMode.Unhandled));
        Assert.That(info.Details?.StackTrace, Does.Contain("Worker.ProcessRequest()"));
        Assert.That(info.Details?.StackTrace, Does.Contain(".<Main>("), "The recorded trace reaches through the bridge rethrow");

        Continue(stopped.ThreadId!.Value);
        WaitForEvent<TerminatedEvent>();
    }

    // Under break-on-all the same crash yields exactly two stops: the first chance at the throw and the
    // final 'unhandled' one. The bridge rethrow adds no stop - '<Main>' has no sequence points, so it is
    // not user code - and the 'user-unhandled' report stays hidden without its filter
    [Test]
    public void BreakOnAllCrashStopsTest() {
        Launch();
        SetExceptionBreakpoints(new[] { "all" }, ("all", null));
        ConfigurationDone();

        var stops = new List<StoppedEvent>();
        for (var i = 0; i < 10; i++) {
            var debugEvent = WaitForEvent<DebugEvent>(it => it is TerminatedEvent || it is StoppedEvent { Reason: StoppedEvent.ReasonValue.Exception });
            if (debugEvent is TerminatedEvent)
                break;
            var stopped = (StoppedEvent)debugEvent;
            stops.Add(stopped);
            Continue(stopped.ThreadId!.Value);
        }

        Assert.That(stops.Select(it => it.Text), Is.EqualTo(new[] {
            $"Exception thrown: 'System.InvalidOperationException' in {ProjectName}.dll",
            "An unhandled exception of type 'System.InvalidOperationException' occurred in System.Private.CoreLib.dll",
        }));
    }
}
