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

    [Test]
    public void BreakOnAllExceptionsTest() {
        Launch();
        SetExceptionBreakpoints(new[] { "all" }, ("all", null));
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(stopped.Text, Is.EqualTo($"Exception thrown: 'System.InvalidOperationException' in {ProjectName}.dll"), "The 'stopped' text is the full message with the throwing module");
        Assert.That(stopped.Description, Is.Null, "Microsoft's debugger sends no 'description' for exception stops");

        var exceptionInfo = Host.SendRequestSync(new ExceptionInfoRequest() { ThreadId = stopped.ThreadId!.Value });
        Assert.That(exceptionInfo.ExceptionId, Is.EqualTo("CLR/System.InvalidOperationException"));
        Assert.That(exceptionInfo.Description, Is.EqualTo($"Exception thrown: 'System.InvalidOperationException' in {ProjectName}.dll: 'comparer boom'"));
        Assert.That(exceptionInfo.Details?.Message, Is.EqualTo("comparer boom"));
    }

    [Test]
    public void ExceptionDetailsAreReportedInFullTest() {
        Launch();
        SetExceptionBreakpoints(new[] { "all" }, ("all", null));
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        var details = Host.SendRequestSync(new ExceptionInfoRequest() { ThreadId = stopped.ThreadId!.Value }).Details;

        Assert.That(details?.HResult, Is.EqualTo(unchecked((int)0x80131509)), "COR_E_INVALIDOPERATION");
        Assert.That(details?.Source, Is.EqualTo(ProjectName), "The assembly that raised it, not a source file");
        Assert.That(details?.FormattedDescription, Does.Contain("comparer boom"));
        // The trace is built by the debugger from the frames the exception passed through, like Microsoft's debugger does -
        // the in-process StackTrace property would hide [StackTraceHidden] frames and see no line information
        Assert.That(details?.StackTrace, Does.StartWith("   at "));
        Assert.That(details?.StackTrace, Does.Contain(":line "), "The user frame carries its source file and line");
    }

    [Test]
    public void UserUnhandledExceptionTest() {
        Launch();
        SetExceptionBreakpoints(new[] { "user-unhandled" }, ("user-unhandled", null));
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(stopped.Text, Is.EqualTo($"An exception of type 'System.InvalidOperationException' occurred in {ProjectName}.dll but was not handled in user code"));

        // The throwing user frame must be on top of the stack
        var frame = GetTopStackFrame(stopped.ThreadId!.Value);
        Assert.That(frame.Name, Does.Contain(ProjectName));

        Continue(stopped.ThreadId!.Value);
        WaitForEvent<TerminatedEvent>();
        var exceptionStops = ReceivedEvents.OfType<StoppedEvent>().Count(it => it.Reason == StoppedEvent.ReasonValue.Exception);
        Assert.That(exceptionStops, Is.EqualTo(1), "The exception caught by the user code must not stop the execution");
    }

    [Test]
    public void ExceptionVariableTest() {
        Launch();
        SetExceptionBreakpoints(new[] { "all" }, ("all", null));
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        var exception = GetLocalVariables(stopped.ThreadId!.Value).FirstOrDefault(it => it.Name.StartsWith("$exception"));
        Assert.That(exception, Is.Not.Null, "The stopped frame exposes the '$exception' variable");
        Assert.That(exception!.Name, Is.EqualTo("$exception [InvalidOperationException]"));
        // The value shows the exception's ToString: the type, the message and the recorded trace
        Assert.That(exception.Value, Does.StartWith("{System.InvalidOperationException: comparer boom"));
        Assert.That(exception.Value, Does.Contain("   at "), "The recorded frames are part of the value");
        Assert.That(exception.Value, Does.EndWith("}"));
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
        SetExceptionBreakpoints(Array.Empty<string>(), ("user-unhandled", "!System.InvalidOperationException"));
        ConfigurationDone();

        WaitForEvent<TerminatedEvent>();
        Assert.That(ReceivedEvents.OfType<StoppedEvent>().Any(it => it.Reason == StoppedEvent.ReasonValue.Exception), Is.False,
            "The ignored exception type must not stop the execution");
    }
}
