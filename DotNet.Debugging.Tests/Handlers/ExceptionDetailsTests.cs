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

    // A fault raised through the runtime's managed throw helper: it is attributed to the faulting user
    // method, the helper frame stays in the trace (its type is [StackTraceHidden], its method is not), and
    // 'Source' reports the core library - the property the exception object itself carries
    [Test]
    public void ThrowHelperAttributionTest() {
        Launch();
        SetExceptionBreakpoints(new[] { "all" }, ("all", null));
        ConfigurationDone();

        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Assert.That(stopped.Text, Is.EqualTo($"Exception thrown: 'System.DivideByZeroException' in {ProjectName}.dll"));

        var details = Host.SendRequestSync(new ExceptionInfoRequest() { ThreadId = stopped.ThreadId!.Value }).Details;
        Assert.That(details?.StackTrace, Does.StartWith("   at Internal.Runtime.CompilerHelpers.ThrowHelpers.ThrowDivideByZeroException()"));
        Assert.That(details?.Source, Is.EqualTo("System.Private.CoreLib"));
    }

    // A wrapper over a wrapper: Microsoft's debugger nests the whole chain in 'innerException', shows the innermost
    // exception's recorded trace as the top-level trace and names the innermost in the description
    [Test]
    public void WrappedExceptionReportsChainTest() {
        Launch();
        SetExceptionBreakpoints(new[] { "all" }, ("all", null));
        ConfigurationDone();

        // The divide fault, the innermost throw, the middle wrapper - then the outermost wrapper
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Continue(stopped.ThreadId!.Value);
        stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        var innermostStop = Host.SendRequestSync(new ExceptionInfoRequest() { ThreadId = stopped.ThreadId!.Value });
        Assert.That(innermostStop.ExceptionId, Is.EqualTo("CLR/System.ArgumentOutOfRangeException"));
        Assert.That(innermostStop.Details?.InnerException, Is.Null.Or.Empty, "An exception without an inner one sends no 'innerException'");
        Continue(stopped.ThreadId!.Value);
        stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        Continue(stopped.ThreadId!.Value);

        stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
        var wrapperInfo = Host.SendRequestSync(new ExceptionInfoRequest() { ThreadId = stopped.ThreadId!.Value });
        Assert.That(wrapperInfo.ExceptionId, Is.EqualTo("CLR/System.InvalidOperationException"));
        Assert.That(wrapperInfo.Description, Does.StartWith($"Exception thrown: 'System.InvalidOperationException' in {ProjectName}.dll: 'failed to open document'"));
        Assert.That(wrapperInfo.Description, Does.Contain("\n Inner exceptions found, see $exception in variables window for more details."));
        Assert.That(wrapperInfo.Description, Does.Contain("\n Innermost exception \t System.ArgumentOutOfRangeException : magic out of range (Parameter 'offset')"),
            "The description names the innermost exception of the chain, with its property message");

        var middle = wrapperInfo.Details?.InnerException?.SingleOrDefault();
        Assert.That(middle, Is.Not.Null, "The direct inner exception is nested in the details");
        Assert.That(middle!.FullTypeName, Is.EqualTo("System.FormatException"));
        Assert.That(middle.Message, Is.EqualTo("bad header"));
        Assert.That(middle.StackTrace, Does.StartWith("   at Faulty.ReadHeader()"), "Each level of the chain carries its own recorded trace");

        var innermost = middle.InnerException?.SingleOrDefault();
        Assert.That(innermost, Is.Not.Null, "The chain nests level by level");
        Assert.That(innermost!.FullTypeName, Is.EqualTo("System.ArgumentOutOfRangeException"));
        Assert.That(innermost.StackTrace, Does.StartWith("   at Faulty.DecodeMagic()"));
        Assert.That(innermost.InnerException, Is.Null.Or.Empty);

        Assert.That(wrapperInfo.Details?.Message, Is.EqualTo("failed to open document"), "The details describe the wrapper");
        Assert.That(wrapperInfo.Details?.StackTrace, Is.EqualTo(innermost.StackTrace), "The top-level trace is the innermost exception's recorded trace, like Microsoft's debugger shows");
    }

    // An AggregateException follows the plain 'InnerException' chain (its first inner). Microsoft's
    // debugger shows the bare '_message' field in the description, we show the 'Message' property
    // everywhere - an accepted difference, the property appends the inner messages
    [Test]
    public void AggregateExceptionTest() {
        Launch();
        SetExceptionBreakpoints(new[] { "all" }, ("all", null));
        ConfigurationDone();

        StoppedEvent stopped;
        ExceptionInfoResponse info;
        while (true) {
            stopped = WaitForStopped(StoppedEvent.ReasonValue.Exception);
            info = Host.SendRequestSync(new ExceptionInfoRequest() { ThreadId = stopped.ThreadId!.Value });
            if (info.ExceptionId == "CLR/System.AggregateException")
                break;
            Continue(stopped.ThreadId!.Value);
        }

        Assert.That(info.Description, Does.StartWith($"Exception thrown: 'System.AggregateException' in {ProjectName}.dll: 'both failed (first boom) (second boom)'"));
        Assert.That(info.Description, Does.Contain("\n Innermost exception \t System.InvalidOperationException : first boom"));
        Assert.That(info.Details?.Message, Is.EqualTo("both failed (first boom) (second boom)"), "The details show the full property message");
        Assert.That(info.Details?.StackTrace, Is.Null, "The innermost exception was never thrown, so the substituted trace is absent");

        var inner = info.Details?.InnerException?.SingleOrDefault();
        Assert.That(inner, Is.Not.Null, "Only the first inner - the 'InnerException' property - is listed");
        Assert.That(inner!.FullTypeName, Is.EqualTo("System.InvalidOperationException"));
        Assert.That(inner.Message, Is.EqualTo("first boom"));
        Assert.That(inner.StackTrace, Is.Null, "An exception that was never thrown has no recorded trace");
    }
}
