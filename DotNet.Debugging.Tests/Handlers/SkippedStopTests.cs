using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// A breakpoint or a Debugger.Break() reached while the debugger evaluates - a DebuggerDisplay, a ToString(), the
// constructor of a DebuggerTypeProxy, a property getter of the Variables view, an expression, a breakpoint condition -
// cannot stop: the evaluation runs the debuggee and has to complete first. The stop is passed, as vsdbg and Visual
// Studio pass it, the evaluation's side effects stay, and the console says so, so the stop that went missing does not
// take a while to explain
public class SkippedStopTests : BaseDebugTestFixture {
    public SkippedStopTests() : base(nameof(SkippedStopTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        using System.Diagnostics;

        var holder = new Holder();
        var counter = new Counter();
        var proxied = new Proxied();
        var ticket = new Ticket();
        var reader = new Reader();
        Console.WriteLine("stop here"); // marker:stop
        Console.WriteLine(holder.Columns);
        Console.WriteLine(counter.Count);
        Console.WriteLine(ticket.ToString());
        Console.WriteLine(reader.Total);
        Console.WriteLine(new ProxiedView(proxied).Items.Length);
        Console.WriteLine("end"); // marker:end

        // The display materializes lazily loaded state through a Debugger.Break(), once
        [DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
        class Holder {
            bool materialized;
            public int Columns { get { Ensure(); return 1; } }
            void Ensure() {
                if (materialized)
                    return;
                Debugger.Log(0, "Op", "Ensure\n");
                Debugger.Break(); // marker:break
                materialized = true;
            }
            string GetDebuggerDisplay() => $"{Columns} columns";
        }
        [DebuggerDisplay("{Count} counted")]
        class Counter {
            public int Count {
                get {
                    var count = 3; // marker:counter
                    return count;
                }
            }
        }
        [DebuggerTypeProxy(typeof(ProxiedView))]
        class Proxied {
            public int[] Items = { 1, 2 };
        }
        class ProxiedView {
            private readonly Proxied proxied;
            public ProxiedView(Proxied proxied) {
                this.proxied = proxied; // marker:proxy
            }
            public int[] Items => proxied.Items;
        }
        class Ticket {
            public override string ToString() {
                var text = "ticket"; // marker:tostring
                return text;
            }
        }
        class Reader {
            public int Total {
                get {
                    var total = 7; // marker:getter
                    return total;
                }
            }
        }
        """;
    }

    private int LaunchToStop(params string[] markers) {
        Launch();
        SetBreakpoints(markers.Select(GetMarkerLine).ToArray());
        ConfigurationDone();
        return WaitForStopped(StoppedEvent.ReasonValue.Breakpoint).ThreadId!.Value;
    }
    private Variable GetLocal(int threadId, string name) {
        return GetLocalVariables(threadId).First(it => it.Name == name);
    }
    // The notice for the marker line: a listing of the locals renders the holder's display first, whose skipped
    // Debugger.Break() is reported ahead of the breakpoint the test is after
    private void ExpectSkippedBreakpoint(string marker) {
        var expected = $"The breakpoint was skipped: it was hit during a function evaluation ({ProgramFilePath}:{GetMarkerLine(marker)}){Environment.NewLine}";
        var output = WaitForEvent<OutputEvent>(it => it.Output == expected);
        Assert.That(output.Category, Is.EqualTo(OutputEvent.CategoryValue.Console));
    }
    private void ExpectBreakpointStop(int threadId, string marker) {
        Continue(threadId);
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine(marker)), "The breakpoint is live outside evaluations");
    }

    // The scenario of issue #5: the Variables view alone runs the display, which runs the materialization, whose
    // Debugger.Break() is skipped; the program's own call then finds the state materialized and never breaks
    [Test]
    public void DebuggerBreakReachedByDisplayIsSkippedTest() {
        var threadId = LaunchToStop("marker:stop");
        Assert.That(GetLocal(threadId, "holder [Holder]").Value, Is.EqualTo("1 columns"), "The display ran through the Debugger.Break()");

        var log = WaitForEvent<OutputEvent>(it => it.Output.StartsWith("Ensure"));
        Assert.That(log.Category, Is.EqualTo(OutputEvent.CategoryValue.Console), "Debugger.Log reaches the console whatever runs the code");
        ExpectSkippedBreakpoint("marker:break");

        Continue(threadId);
        Assert.That(CollectStopsUntilExit(), Is.Empty, "The materialization is done, the program's own call returns before the Debugger.Break()");
        Assert.That(ReceivedEvents.OfType<OutputEvent>().Count(it => it.Output.StartsWith("Ensure")), Is.EqualTo(1), "The materialization ran once, for the display");
    }

    [Test]
    public void DebuggerBreakStopsWhenNothingEvaluatesTest() {
        var threadId = LaunchToStop("marker:stop");
        Continue(threadId);
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Pause);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:break")));
    }

    [Test]
    public void BreakpointReachedByDisplayIsSkippedTest() {
        var threadId = LaunchToStop("marker:stop", "marker:counter");
        Assert.That(GetLocal(threadId, "counter [Counter]").Value, Is.EqualTo("3 counted"));
        ExpectSkippedBreakpoint("marker:counter");
        ExpectBreakpointStop(threadId, "marker:counter");
    }

    [Test]
    public void BreakpointReachedByToStringIsSkippedTest() {
        var threadId = LaunchToStop("marker:stop", "marker:tostring");
        Assert.That(GetLocal(threadId, "ticket [Ticket]").Value, Is.EqualTo("{ticket}"));
        ExpectSkippedBreakpoint("marker:tostring");
        ExpectBreakpointStop(threadId, "marker:tostring");
    }

    [Test]
    public void BreakpointReachedByProxyConstructorIsSkippedTest() {
        var threadId = LaunchToStop("marker:stop", "marker:proxy");
        var proxied = GetLocal(threadId, "proxied [Proxied]");
        var members = GetVariables(proxied.VariablesReference);
        Assert.That(members.Select(it => it.Name), Is.EqualTo(new[] { "Items [int[]]", "Raw View" }), "The proxy's members stand in for the value's");
        ExpectSkippedBreakpoint("marker:proxy");
        ExpectBreakpointStop(threadId, "marker:proxy");
    }

    [Test]
    public void BreakpointReachedByGetterIsSkippedTest() {
        var threadId = LaunchToStop("marker:stop", "marker:getter");
        var reader = GetLocal(threadId, "reader [Reader]");
        var total = GetVariables(reader.VariablesReference).First(it => it.Name == "Total [int]");
        Assert.That(total.Value, Is.EqualTo("7"));
        ExpectSkippedBreakpoint("marker:getter");
        ExpectBreakpointStop(threadId, "marker:getter");
    }

    [Test]
    public void BreakpointReachedByExpressionIsSkippedTest() {
        var threadId = LaunchToStop("marker:stop", "marker:getter");
        Assert.That(Evaluate("reader.Total", threadId).Result, Is.EqualTo("7"));
        ExpectSkippedBreakpoint("marker:getter");
    }

    [Test]
    public void BreakpointReachedByConditionIsSkippedTest() {
        Launch();
        SetBreakpoints(
            new SourceBreakpoint() { Line = GetMarkerLine("marker:stop"), Condition = "reader.Total > 0" },
            new SourceBreakpoint() { Line = GetMarkerLine("marker:getter") });
        ConfigurationDone();

        ExpectSkippedBreakpoint("marker:getter");
        var stopped = WaitForStopped(StoppedEvent.ReasonValue.Breakpoint);
        Assert.That(GetTopStackFrame(stopped.ThreadId!.Value).Line, Is.EqualTo(GetMarkerLine("marker:stop")), "The condition held, its own breakpoint stops");
    }
}
