using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class FunctionBreakpointTests : BaseDebugTestFixture {
    public FunctionBreakpointTests() : base(nameof(FunctionBreakpointTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        Worker.Process(1);
        Worker.Process("two");
        Helper.Run();
        Console.WriteLine("done"); // marker:end

        public static class Worker {
            public static void Process(int number) { // marker:processInt
                Console.WriteLine(number);
            }
            public static void Process(string text) { // marker:processString
                Console.WriteLine(text);
            }
        }
        public static class Helper {
            public static void Run() { // marker:runHeader
                Console.WriteLine("run");
            }
        }
        """;
    }

    private List<Breakpoint> SetFunctionBreakpoints(params string[] names) {
        var response = Host.SendRequestSync(new SetFunctionBreakpointsRequest() {
            Breakpoints = names.Select(it => new FunctionBreakpoint() { Name = it }).ToList(),
        });
        return response.Breakpoints;
    }

    [Test]
    public void FunctionBreakpointByNameTest() {
        Launch();
        var breakpoints = SetFunctionBreakpoints("Helper.Run");
        Assert.That(breakpoints[0].Verified, Is.False, "A function breakpoint cannot bind before the module loads");
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Name, Does.Contain("Run"));
        Assert.That(frame.Line, Is.EqualTo(GetMarkerLine("marker:runHeader")), "The breakpoint binds at the method entry");
    }

    [Test]
    public void OverloadsBindTogetherTest() {
        Launch();
        SetFunctionBreakpoints("Worker.Process");
        ConfigurationDone();

        // Both overloads are called once, the shared breakpoint stops at each call
        var first = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(first.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:processInt")));
        Continue(first.ThreadId!.Value);

        var second = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(second.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:processString")));
    }

    [Test]
    public void ParameterListSelectsTheOverloadTest() {
        Launch();
        SetFunctionBreakpoints("Worker.Process(string)");
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:processString")),
            "Only the overload matching the parameter list stops");

        Continue(stopped.ThreadId!.Value);
        WaitForEvent<TerminatedEvent>();
    }

    [Test]
    public void UnknownFunctionStaysUnverifiedTest() {
        Launch();
        SetFunctionBreakpoints("No.Such.Method");
        ConfigurationDone();

        // The program runs to completion, the breakpoint never binds and never stops anything
        Assert.That(CollectStopsUntilExit(), Is.Empty);
        var breakpointEvents = ReceivedEvents.OfType<BreakpointEvent>().Where(it => it.Breakpoint.Verified);
        Assert.That(breakpointEvents, Is.Empty, "A function breakpoint without a matching method must not verify");
    }
}
