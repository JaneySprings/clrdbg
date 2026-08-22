using System.Collections.Concurrent;
using System.IO.Pipes;
using DotNet.Debugging.Adapter;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using DapExceptionFilterOptions = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.ExceptionFilterOptions;

namespace DotNet.Debugging.Tests;

[TestFixture]
public abstract class BaseDebugTestFixture {
    protected string SandboxDirectory { get; init; }
    protected string ProjectName { get; init; }

    protected string ProjectDirectory { get; private set; } = null!;
    protected string ProgramFilePath { get; private set; } = null!;
    protected string ProgramPath { get; private set; } = null!;

    protected DebugProtocolHost Host { get; private set; } = null!;

    private DebugSession debugSession = null!;
    private AnonymousPipeServerStream hostInput = null!;
    private AnonymousPipeServerStream hostOutput = null!;
    private AnonymousPipeClientStream adapterInput = null!;
    private AnonymousPipeClientStream adapterOutput = null!;
    private BlockingCollection<DebugEvent> eventQueue = null!;
    private ConcurrentQueue<DebugEvent> receivedEvents = null!;

    protected IReadOnlyCollection<DebugEvent> ReceivedEvents => receivedEvents;

    protected BaseDebugTestFixture(string name) {
        ProjectName = name;
        SandboxDirectory = Path.Combine(AppContext.BaseDirectory, "Sandbox");
    }

    protected abstract string CreateProgramFileContent();
    // Additional files the debuggee project needs, written before it is built
    protected virtual void CreateProjectFiles(string projectDirectory) { }
    protected virtual string CreateProjectFileContent() {
        return """
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RollForward>major</RollForward>
                <NoWarn>$(NoWarn);CS0414;CS0169;CS0219</NoWarn>
            </PropertyGroup>
        </Project>
        """;
    }

    [OneTimeSetUp]
    public void GlobalSetup() {
        ProjectDirectory = Path.Combine(SandboxDirectory, ProjectName);
        if (Directory.Exists(ProjectDirectory))
            Directory.Delete(ProjectDirectory, true);

        Directory.CreateDirectory(ProjectDirectory);
        ProgramFilePath = Path.Combine(ProjectDirectory, "Program.cs");
        File.WriteAllText(Path.Combine(ProjectDirectory, $"{ProjectName}.csproj"), CreateProjectFileContent());
        File.WriteAllText(ProgramFilePath, CreateProgramFileContent());
        CreateProjectFiles(ProjectDirectory);

        var buildProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet", "build -c Debug -nologo") {
            WorkingDirectory = ProjectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        Assert.That(buildProcess, Is.Not.Null);
        var buildOutput = buildProcess!.StandardOutput.ReadToEnd();
        buildProcess.WaitForExit();
        Assert.That(buildProcess.ExitCode, Is.EqualTo(0), $"Failed to build the debuggee:{Environment.NewLine}{buildOutput}");

        ProgramPath = Path.Combine(ProjectDirectory, "bin", "Debug", "net10.0", $"{ProjectName}.dll");
        Assert.That(File.Exists(ProgramPath), $"Debuggee assembly not found: {ProgramPath}");
    }

    [OneTimeTearDown]
    public void GlobalTearDown() {
        try {
            Directory.Delete(ProjectDirectory, true);
        }
        catch { /* best effort */ }
    }

    [SetUp]
    public void SetUp() {
        eventQueue = new BlockingCollection<DebugEvent>();
        receivedEvents = new ConcurrentQueue<DebugEvent>();

        hostInput = new AnonymousPipeServerStream(PipeDirection.Out);
        adapterInput = new AnonymousPipeClientStream(PipeDirection.In, hostInput.ClientSafePipeHandle);
        hostOutput = new AnonymousPipeServerStream(PipeDirection.In);
        adapterOutput = new AnonymousPipeClientStream(PipeDirection.Out, hostOutput.ClientSafePipeHandle);

        debugSession = new DebugSession(adapterInput, adapterOutput);
        debugSession.Start();

        Host = new DebugProtocolHost(hostInput, hostOutput);
        Host.EventReceived += OnEventReceived;
        Host.Run();
        Host.SendRequestSync(new InitializeRequest() {
            AdapterID = "meteor-v2",
            LinesStartAt1 = true,
            ColumnsStartAt1 = true,
        });
    }

    [TearDown]
    public void TearDown() {
        try {
            Host.SendRequestSync(new DisconnectRequest() { TerminateDebuggee = true });
        }
        catch { /* the session may be terminated already */ }
        try {
            Host.Stop();
        }
        catch { /* best effort */ }

        hostInput.Dispose();
        hostOutput.Dispose();
        adapterInput.Dispose();
        adapterOutput.Dispose();
        eventQueue.Dispose();
    }

    private void OnEventReceived(object? sender, EventReceivedEventArgs args) {
        if (args.Body is not DebugEvent debugEvent)
            return;

        receivedEvents.Enqueue(debugEvent);
        try {
            eventQueue.Add(debugEvent);
        }
        catch (ObjectDisposedException) {
            // Events may still arrive while the session is being torn down
        }
    }

    /// <summary>Launches the program. 'skipDebug' runs it without a debugger at all, 'properties' are added to the launch configuration.</summary>
    protected void Launch(bool stopAtEntry = false, bool justMyCode = true, bool skipDebug = false, Dictionary<string, JToken>? properties = null) {
        var launchRequest = new LaunchRequest();
        launchRequest.ConfigurationProperties = new Dictionary<string, JToken> {
            ["program"] = ProgramPath,
            ["stopAtEntry"] = stopAtEntry,
            ["justMyCode"] = justMyCode,
            ["skipDebug"] = skipDebug,
        };
        foreach (var (key, value) in properties ?? new Dictionary<string, JToken>())
            launchRequest.ConfigurationProperties[key] = value;

        Host.SendRequestSync(launchRequest);
    }
    /// <summary>
    /// Attaches to a process started outside the debugger. The attach itself is deferred until
    /// 'ConfigurationDone', the same as a launch.
    /// </summary>
    protected void Attach(int processId, bool justMyCode = true) {
        var attachRequest = new AttachRequest();
        attachRequest.ConfigurationProperties = new Dictionary<string, JToken> {
            ["processId"] = processId,
            ["justMyCode"] = justMyCode,
        };
        Host.SendRequestSync(attachRequest);
    }
    /// <summary>
    /// Starts the debuggee outside the debugger, for the attach tests, and counts what it prints. Only
    /// a launched program has its streams redirected into the adapter, so an attached one's output
    /// arrives here rather than as OutputEvents - which is what lets a test tell a process that is
    /// really suspended from one that is only reported as such.
    /// </summary>
    protected Debuggee StartDebuggee() {
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("dotnet", ProgramPath) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        Assert.That(process, Is.Not.Null);

        var debuggee = new Debuggee(process!);
        debuggee.BeginReading();
        return debuggee;
    }

    protected sealed class Debuggee : IDisposable {
        private readonly System.Diagnostics.Process process;
        private int printedLines;

        public Debuggee(System.Diagnostics.Process process) {
            this.process = process;
        }

        public int Id => process.Id;
        public int PrintedLines => System.Threading.Volatile.Read(ref printedLines);

        public void BeginReading() {
            // Drained continuously: a full pipe would block the debuggee, which looks exactly like a
            // process suspended by the debugger
            process.OutputDataReceived += (_, args) => {
                if (args.Data != null)
                    System.Threading.Interlocked.Increment(ref printedLines);
            };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (PrintedLines == 0) {
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException("The debuggee printed nothing, so it never got going");
                System.Threading.Thread.Sleep(25);
            }
        }

        /// <summary>How many lines the debuggee printed during the given window. Zero means suspended.</summary>
        public int CountPrintedDuring(TimeSpan window) {
            var before = PrintedLines;
            System.Threading.Thread.Sleep(window);
            return PrintedLines - before;
        }

        public void Dispose() {
            try {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch { /* already gone */ }
            finally {
                process.Dispose();
            }
        }
    }

    protected List<Breakpoint> SetBreakpoints(params SourceBreakpoint[] breakpoints) {
        var response = Host.SendRequestSync(new SetBreakpointsRequest() {
            Source = new Source() { Path = ProgramFilePath },
            Breakpoints = breakpoints.ToList(),
        });
        return response.Breakpoints;
    }
    protected List<Breakpoint> SetBreakpoints(params int[] lines) {
        return SetBreakpoints(lines.Select(line => new SourceBreakpoint() { Line = line }).ToArray());
    }
    protected List<Breakpoint> SetExceptionBreakpoints(string[] filters, params (string FilterId, string? Condition)[] filterOptions) {
        var response = Host.SendRequestSync(new SetExceptionBreakpointsRequest() {
            Filters = filters.ToList(),
            FilterOptions = filterOptions.Select(it => new DapExceptionFilterOptions() {
                FilterId = it.FilterId,
                Condition = it.Condition,
            }).ToList(),
        });
        return response.Breakpoints ?? new List<Breakpoint>();
    }
    protected void ConfigurationDone() {
        Host.SendRequestSync(new ConfigurationDoneRequest());
    }
    protected void Continue(int threadId) {
        Host.SendRequestSync(new ContinueRequest() { ThreadId = threadId });
    }

    protected TEvent WaitForEvent<TEvent>(Func<TEvent, bool>? predicate = null, int timeoutMs = 30000) where TEvent : DebugEvent {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline) {
            if (!eventQueue.TryTake(out var debugEvent, 500))
                continue;
            if (debugEvent is TEvent typedEvent && (predicate == null || predicate(typedEvent)))
                return typedEvent;
        }
        throw new TimeoutException($"Timed out waiting for the '{typeof(TEvent).Name}' event");
    }
    protected StoppedEvent WaitForStopped(StoppedEvent.ReasonValue? reason = null) {
        return WaitForEvent<StoppedEvent>(it => reason == null || it.Reason == reason);
    }
    /// <summary>
    /// The first thread of an attached debuggee, which is the point a client can do anything with it.
    /// 'PerformAttach' is fire-and-forget, so the attach is still landing when 'ConfigurationDone'
    /// returns and there is nothing to act on until this answers.
    /// </summary>
    protected int WaitForFirstThread(int timeoutMs = 30000) {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline) {
            var threads = Host.SendRequestSync(new ThreadsRequest()).Threads;
            if (threads.Count > 0)
                return threads[0].Id;

            System.Threading.Thread.Sleep(25);
        }
        throw new TimeoutException("The attach never produced any threads");
    }

    protected StackFrame GetTopStackFrame(int threadId) {
        var response = Host.SendRequestSync(new StackTraceRequest() { ThreadId = threadId });
        Assert.That(response.StackFrames, Is.Not.Empty);
        return response.StackFrames[0];
    }
    protected List<Variable> GetLocalVariables(int threadId) {
        var frame = GetTopStackFrame(threadId);
        var scopes = Host.SendRequestSync(new ScopesRequest() { FrameId = frame.Id });
        Assert.That(scopes.Scopes, Is.Not.Empty);
        return GetVariables(scopes.Scopes[0].VariablesReference);
    }
    protected List<Variable> GetVariables(int variablesReference) {
        return Host.SendRequestSync(new VariablesRequest() { VariablesReference = variablesReference }).Variables;
    }
    protected EvaluateResponse Evaluate(string expression, int threadId) {
        var frame = GetTopStackFrame(threadId);
        return Host.SendRequestSync(new EvaluateRequest() { Expression = expression, FrameId = frame.Id });
    }

    protected int GetMarkerLine(string marker) {
        var lines = File.ReadAllLines(ProgramFilePath);
        for (var i = 0; i < lines.Length; i++) {
            if (lines[i].Contains(marker, StringComparison.Ordinal))
                return i + 1;
        }
        throw new InvalidOperationException($"Marker '{marker}' not found in '{ProgramFilePath}'");
    }
}