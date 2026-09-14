using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

public class ExceptionDetailsTests : BaseDebugTestFixture {
    public ExceptionDetailsTests() : base(nameof(ExceptionDetailsTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        try {
            var _ = Faulty.Divide(10, 0);
        } catch (Exception ex) {
            Console.WriteLine($"caught: {ex.GetType().Name}");
        }
        try {
            Faulty.OpenDocument();
        } catch (Exception ex) {
            Console.WriteLine($"caught: {ex.GetType().Name} / inner: {ex.InnerException?.GetType().Name}");
        }
        try {
            Faulty.ThrowAggregate();
        } catch (Exception ex) {
            Console.WriteLine($"caught: {ex.GetType().Name}");
        }
        Console.WriteLine("done");

        public static class Faulty {
            public static int Divide(int left, int right) {
                return left / right;
            }
            public static void OpenDocument() {
                try {
                    ReadHeader();
                } catch (Exception inner) {
                    throw new InvalidOperationException("failed to open document", inner);
                }
            }
            private static void ReadHeader() {
                try {
                    DecodeMagic();
                } catch (Exception inner) {
                    throw new FormatException("bad header", inner);
                }
            }
            private static void DecodeMagic() {
                throw new ArgumentOutOfRangeException("offset", "magic out of range");
            }
            public static void ThrowAggregate() {
                throw new AggregateException("both failed",
                    new InvalidOperationException("first boom"),
                    new FormatException("second boom"));
            }
        }
        """;
    }

    // A divide fault is attributed to the faulting user method on every architecture. On arm64 the JIT raises it
    // through a managed throw helper whose type is hidden from stack traces, on x64 the hardware fault surfaces in
    // the user method itself: the reported trace starts at the user method either way
    [Test]
    public void ThrowHelperAttributionTest() {
        LaunchWithExceptionFilters("all");

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(stopped.Text, Is.EqualTo($"Exception thrown: 'System.DivideByZeroException' in {ProjectName}.dll"));

        var details = GetExceptionInfo(stopped.ThreadId!.Value).Details;
        Assert.That(details?.StackTrace, Does.StartWith("   at Faulty.Divide(int left, int right) in "), "The faulting user frame carries its source");
        Assert.That(details?.StackTrace, Does.Not.Contain("ThrowHelpers"), "A throw helper hidden through its type is left out");
    }

    // A wrapper over a wrapper: the details nest the direct inner exception and the description names it
    [Test]
    public void WrappedExceptionReportsInnerTest() {
        LaunchWithExceptionFilters("all");

        // The divide fault, the innermost throw, the middle wrapper - then the outermost wrapper
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Continue(stopped.ThreadId!.Value);
        stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        var innermostStop = GetExceptionInfo(stopped.ThreadId!.Value);
        Assert.That(innermostStop.ExceptionId, Is.EqualTo("System.ArgumentOutOfRangeException"));
        Assert.That(innermostStop.Details?.InnerException, Is.Null.Or.Empty, "An exception without an inner one sends no 'innerException'");
        Continue(stopped.ThreadId!.Value);
        stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Continue(stopped.ThreadId!.Value);

        stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        var wrapperInfo = GetExceptionInfo(stopped.ThreadId!.Value);
        Assert.That(wrapperInfo.ExceptionId, Is.EqualTo("System.InvalidOperationException"));
        Assert.That(wrapperInfo.Description, Is.EqualTo($"Exception thrown: 'System.InvalidOperationException' in {ProjectName}.dll: 'failed to open document'\nInner exception: System.FormatException: bad header"),
            "The description names the wrapped exception, with its property message");

        var inner = wrapperInfo.Details?.InnerException?.SingleOrDefault();
        Assert.That(inner, Is.Not.Null, "The direct inner exception is nested in the details");
        Assert.That(inner!.FullTypeName, Is.EqualTo("System.FormatException"));
        Assert.That(inner.Message, Is.EqualTo("bad header"));
        Assert.That(inner.StackTrace, Does.StartWith("   at Faulty.ReadHeader()"), "The inner exception carries its own recorded trace");
        Assert.That(inner.InnerException, Is.Null.Or.Empty, "Only the direct inner exception is read, the rest of the chain is reachable through $exception");

        Assert.That(wrapperInfo.Details?.Message, Is.EqualTo("failed to open document"), "The details describe the wrapper");
        Assert.That(wrapperInfo.Details?.StackTrace, Does.StartWith("   at Faulty.OpenDocument()"), "The top-level trace is the wrapper's own");
    }

    // An AggregateException reports its first inner, the 'InnerException' property. The description shows the
    // 'Message' property, which appends the inner messages
    [Test]
    public void AggregateExceptionTest() {
        LaunchWithExceptionFilters("all");

        StoppedEvent stopped;
        ExceptionInfoResponse info;
        while (true) {
            stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
            info = GetExceptionInfo(stopped.ThreadId!.Value);
            if (info.ExceptionId == "System.AggregateException")
                break;
            Continue(stopped.ThreadId!.Value);
        }

        Assert.That(info.Description, Is.EqualTo($"Exception thrown: 'System.AggregateException' in {ProjectName}.dll: 'both failed (first boom) (second boom)'\nInner exception: System.InvalidOperationException: first boom"));
        Assert.That(info.Details?.Message, Is.EqualTo("both failed (first boom) (second boom)"), "The details show the full property message");
        Assert.That(info.Details?.StackTrace, Does.StartWith("   at Faulty.ThrowAggregate()"));

        var inner = info.Details?.InnerException?.SingleOrDefault();
        Assert.That(inner, Is.Not.Null, "Only the first inner - the 'InnerException' property - is listed");
        Assert.That(inner!.FullTypeName, Is.EqualTo("System.InvalidOperationException"));
        Assert.That(inner.Message, Is.EqualTo("first boom"));
        Assert.That(inner.StackTrace, Is.Null, "An exception that was never thrown has no recorded trace");
    }
}
