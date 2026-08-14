using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class ExceptionTests : BaseDebugTestFixture {
    public ExceptionTests() : base(nameof(ExceptionTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        var numbers = new List<int> { 3, 1, 2 };
        try {
            // Thrown in user code, caught inside the BCL sort helper -> 'user-unhandled'
            numbers.Sort((left, right) => throw new InvalidOperationException("comparer boom"));
        } catch (Exception ex) {
            Console.WriteLine($"caught: {ex.GetType().Name}");
        }
        Console.WriteLine("done");
        """;
    }

    private static readonly string[] filters = new[] { "all" };

    [Test]
    public void BreakOnAllExceptionsTest() {
        Launch();
        SetExceptionBreakpoints(filters, ("all", null));
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(stopped.Text, Is.EqualTo("System.InvalidOperationException"));

        var exceptionInfo = Host.SendRequestSync(new ExceptionInfoRequest() { ThreadId = stopped.ThreadId!.Value });
        Assert.That(exceptionInfo.ExceptionId, Is.EqualTo("CLR/System.InvalidOperationException"));
        Assert.That(exceptionInfo.Details?.Message, Is.EqualTo("comparer boom"));
    }

    private static readonly string[] filters = new[] { "userUnhandled" };

    [Test]
    public void UserUnhandledExceptionTest() {
        Launch();
        SetExceptionBreakpoints(filters, ("userUnhandled", null));
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(stopped.Text, Is.EqualTo("System.InvalidOperationException"));
        Assert.That(stopped.Description, Does.Contain("user-unhandled"));

        // The throwing user frame must be on top of the stack
        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Name, Does.Contain(ProjectName));

        Continue(stopped.ThreadId!.Value);
        WaitForEvent<TerminatedEvent>();
        var exceptionStops = ReceivedEvents.OfType<StoppedEvent>().Count(it => it.Reason == StoppedEvent.ReasonValue.Exception);
        Assert.That(exceptionStops, Is.EqualTo(1), "The exception caught by the user code must not stop the execution");
    }

    [Test]
    public void NoExceptionFiltersTest() {
        Launch();
        SetExceptionBreakpoints(Array.Empty<string>());
        ConfigurationDone();

        WaitForEvent<TerminatedEvent>();
        Assert.That(ReceivedEvents.OfType<StoppedEvent>().Any(it => it.Reason == StoppedEvent.ReasonValue.Exception), Is.False,
            "Handled exceptions must not stop the execution without filters");
    }

    [Test]
    public void ExceptionIgnoreConditionTest() {
        Launch();
        SetExceptionBreakpoints(Array.Empty<string>(), ("userUnhandled", "!System.InvalidOperationException"));
        ConfigurationDone();

        WaitForEvent<TerminatedEvent>();
        Assert.That(ReceivedEvents.OfType<StoppedEvent>().Any(it => it.Reason == StoppedEvent.ReasonValue.Exception), Is.False,
            "The ignored exception type must not stop the execution");
    }
}