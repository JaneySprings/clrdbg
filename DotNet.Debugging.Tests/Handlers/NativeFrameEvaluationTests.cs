using System.Diagnostics;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

// The runtime runs an evaluation only on a thread stopped at a safe point in managed code, at a breakpoint or a step. A
// thread paused while blocked in native code - a P/Invoke, a sleep or a wait, a platform run loop - is marked unsafe in
// its user state, and starting an eval there is refused up front rather than started and left to time out; a listing of
// the thread's variables falls back to type names rather than filling with errors
public class NativeFrameEvaluationTests : BaseDebugTestFixture {
    private Debuggee? debuggee;

    public NativeFrameEvaluationTests() : base(nameof(NativeFrameEvaluationTests)) { }

    protected override string CreateProgramFileContent() {
        return """
        using System.Diagnostics;
        using System.Runtime.InteropServices;

        var sample = new Sample();
        var sleeper = new Thread(() => Thread.Sleep(120000)) { Name = "sleeper" };
        sleeper.Start();
        Console.WriteLine("started");
        Console.Out.Flush();
        if (OperatingSystem.IsWindows())
            Native.Sleep(120000);
        else
            Native.sleep(120);
        Console.WriteLine(sample.Name);

        [DebuggerDisplay("{Name}")]
        class Sample {
            public string Name => "shown";
        }
        static class Native {
            [DllImport("libc")] internal static extern uint sleep(uint seconds);
            [DllImport("kernel32")] internal static extern void Sleep(uint milliseconds);
        }
        """;
    }

    [TearDown]
    public void StopDebuggee() {
        debuggee?.Dispose();
    }

    [Test]
    public void EvaluationOnAThreadInNativeCodeIsRefusedTest() {
        var (mainThreadId, sleeperThreadId) = PauseInNativeCode();
        var mainFrame = GetTopStackFrame(mainThreadId);
        var sleeperFrame = GetTopStackFrame(sleeperThreadId);

        var watch = Stopwatch.StartNew();
        var mainFailure = Assert.Throws<ProtocolException>(() => Host.SendRequestSync(new EvaluateRequest() { Expression = "sample.Name", FrameId = mainFrame.Id }));
        var sleeperFailure = Assert.Throws<ProtocolException>(() => Host.SendRequestSync(new EvaluateRequest() { Expression = "System.Environment.TickCount", FrameId = sleeperFrame.Id }));
        watch.Stop();

        Assert.Multiple(() => {
            Assert.That(mainFailure!.Message, Is.EqualTo("Cannot evaluate the expression: the thread is stopped in native or optimized code, where the runtime cannot run an evaluation"));
            Assert.That(sleeperFailure!.Message, Is.EqualTo("Cannot evaluate the expression: the thread is paused in a sleep, wait, or join"));
            // Refused by the runtime up front, not given up on after the evaluation timeout
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(2000));
        });
    }

    [Test]
    public void VariablesOfAThreadInNativeCodeFallBackToTypeNamesTest() {
        var (mainThreadId, _) = PauseInNativeCode();

        var watch = Stopwatch.StartNew();
        var locals = GetLocalVariables(mainThreadId).ToDictionary(it => it.Name.Split(' ')[0], it => it.Value);
        watch.Stop();

        Assert.Multiple(() => {
            Assert.That(locals["sample"], Is.EqualTo("{Sample}"));
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(2000));
        });
    }

    // Attaches to the debuggee once it is blocked - the main thread in the native sleep, the 'sleeper' in Thread.Sleep - and
    // pauses it. The threads are told apart by name, the main thread being the one that is not the sleeper
    private (int MainThreadId, int SleeperThreadId) PauseInNativeCode() {
        debuggee = StartDebuggee();
        Attach(debuggee.Id);
        ConfigurationDone();
        var firstThreadId = WaitForFirstThread();
        // Let the debuggee reach its blocking calls before pausing it
        System.Threading.Thread.Sleep(1500);
        PauseUntilAccepted(firstThreadId);
        WaitForStopped();

        var threads = Host.SendRequestSync(new ThreadsRequest()).Threads;
        TestContext.Out.WriteLine($"threads: {string.Join(", ", threads.Select(it => $"{it.Id} '{it.Name}'"))}");
        var sleeper = threads.First(it => it.Name.Contains("sleeper", StringComparison.Ordinal));
        var main = threads.First(it => it.Id != sleeper.Id);
        return (main.Id, sleeper.Id);
    }
    // For a short while after the attach has landed the process reports itself as not running, and the pause is refused
    private void PauseUntilAccepted(int threadId) {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true) {
            try {
                Host.SendRequestSync(new PauseRequest() { ThreadId = threadId });
                return;
            }
            catch (ProtocolException) when (DateTime.UtcNow < deadline) {
                System.Threading.Thread.Sleep(25);
            }
        }
    }
}
