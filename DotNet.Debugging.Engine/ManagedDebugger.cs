using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Breakpoints;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Evaluation;
using DotNet.Debugging.Engine.Extensions;
using DotNet.Debugging.Engine.Interop;
using DotNet.Debugging.Engine.Logging;
using DotNet.Debugging.Engine.Models;
using DotNet.Debugging.Engine.Stepping;
using DotNet.Debugging.Engine.Variables;

namespace DotNet.Debugging.Engine;

// An ICorDebug based debugger for .NET (Core) processes. Every request and every runtime callback is handled
// under one lock, in the order they arrive, so the debuggee state a request sees is never stale
public partial class ManagedDebugger {
    // Makes the runtime of a spawned debuggee wait for a diagnostics client, so the attach lands before any managed code runs
    public const string DiagnosticPortSuspendVariable = "DOTNET_DefaultDiagnosticPortSuspend";
    internal const string CoreLibraryName = "System.Private.CoreLib.dll";

    private readonly CorDebugManagedCallback callbacks;
    private readonly Channel<CorDebugManagedCallbackEventArgs> eventQueue;
    private readonly SemaphoreSlim syncLock;
    // Keyed by the runtime's module object: the COM wrappers are one per object for as long as they are held,
    // which a base address is not a substitute for - a dynamic module has none
    private readonly Dictionary<ICorDebugModule, ModuleInfo> modules;
    private readonly Dictionary<int, ICorDebugThread> threads;
    // Threads whose current exception was thrown in, or passed through, user code
    private readonly HashSet<int> exceptionThreads;
    private readonly Dictionary<int, ExceptionStopKind> exceptionStopKinds;
    // The module each thread's current exception is attributed to, captured at the raise: the stop
    // happens later in the dispatch, when the thread's frames no longer show it
    private readonly Dictionary<int, string?> exceptionModules;
    // The address of the exception object that attribution was captured for, to tell its raise repeated from another one's
    private readonly Dictionary<int, ulong> exceptionAddresses;
    private readonly BreakpointManager breakpointManager;
    private readonly VariableManager variableManager;
    private readonly VariableProvider variableProvider;
    private readonly FrameReferenceManager frameReferenceManager;
    private readonly StepController stepController;
    private ICorDebug? corDebug;
    private ICorDebugProcess? process;
    private Process? launchedProcess;
    private StreamWriter? standardInput;
    private ExpressionEvaluator? evaluator;
    private ICorDebugFunctionBreakpoint? entryPointBreakpoint;
    private bool stopAtEntryPending;
    // An exception stop is being reported and the subscriber has not continued it (yet)
    private bool isExceptionStopPending;
    private bool isRemoteAttach;
    private int? mainThreadId;
    private int? oldestThreadId;
    private int nextModuleId;

    public bool JustMyCode { get; set; } = true;
    // A source file matched by name alone (PDBs built from a different location) must also match the PDB's content checksum
    public bool RequireExactSource { get; set; } = true;
    // 'Step over properties and operators': a step never stops inside an accessor or an operator method
    public bool EnableStepFiltering { get; set; } = true;
    // Whether ICorDebug reports the debuggee as executing. A state that cannot be read counts as not running
    public bool IsRunning => process != null && process.TryIsRunning(out var isRunning) == Cor.S_OK && isRunning;
    public int ProcessId { get; private set; }

    internal FuncEvalRunner FuncEval { get; }
    internal IReadOnlyCollection<ModuleInfo> Modules => modules.Values;
    // Incremented whenever a module is loaded or its metadata changes, so everything derived from the module set can detect staleness
    internal int ModulesVersion { get; private set; }
    internal bool IsEvaluating => FuncEval.IsRunning;

    public event Action<StopInfo>? OnStopped;
    // The subscriber decides whether to stop (do nothing) or to 'Continue()' after an exception
    public event Action<ExceptionStopInfo>? OnExceptionThrown;
    public event Action<int>? OnExited;
    public event Action<int>? OnProcessStarted;
    public event Action<int>? OnThreadStarted;
    public event Action<int>? OnThreadExited;
    public event Action<ModuleInfo>? OnModuleLoaded;
    // A module without symbols next to it: the subscriber may locate the PDB and set 'SymbolFilePath'
    public event Action<SymbolsRequest>? OnSymbolsRequested;
    // A launch with a terminal console: the subscriber starts the debuggee in the client's terminal and sets 'ProcessId'
    public event Action<LaunchRequest>? OnTerminalLaunchRequested;
    // Output text of a launched debuggee, 'true' for stderr
    public event Action<string, bool>? OnOutput;
    public event Action<string>? OnLogPoint;
    // A message the debuggee logs to the debugger: Debug.WriteLine, Trace.WriteLine, Debugger.Log
    public event Action<string>? OnDebugMessage;
    public event Action<Breakpoint>? OnBreakpointChanged;

    public ManagedDebugger() {
        callbacks = new CorDebugManagedCallback();
        eventQueue = Channel.CreateUnbounded<CorDebugManagedCallbackEventArgs>(new UnboundedChannelOptions { SingleWriter = true });
        syncLock = new SemaphoreSlim(1, 1);
        modules = new Dictionary<ICorDebugModule, ModuleInfo>(ReferenceEqualityComparer.Instance);
        threads = new Dictionary<int, ICorDebugThread>();
        exceptionThreads = new HashSet<int>();
        exceptionStopKinds = new Dictionary<int, ExceptionStopKind>();
        exceptionModules = new Dictionary<int, string?>();
        exceptionAddresses = new Dictionary<int, ulong>();
        breakpointManager = new BreakpointManager();
        variableManager = new VariableManager();
        variableProvider = new VariableProvider(this, variableManager);
        frameReferenceManager = new FrameReferenceManager();
        stepController = new StepController(this);
        FuncEval = new FuncEvalRunner(WaitForEvalEventAsync);

        callbacks.OnAnyEvent += QueueEvent;
        _ = Task.Run(ProcessEventQueueAsync);
    }

    // Runs a request under the debugger's lock, once the runtime callbacks queued so far have been handled
    public async Task<T> InvokeAsync<T>(Func<Task<T>> action) {
        await syncLock.WaitAsync();
        try {
            while (eventQueue.Reader.TryRead(out var callbackEvent))
                await DispatchEventAsync(callbackEvent);
            return await action();
        }
        finally {
            syncLock.Release();
        }
    }

    // The debuggee runs from the moment it is started, so the host starts it once the breakpoints are set:
    // a client sends them only after its launch or attach request and expects them bound from the first line
    public async Task LaunchAsync(LaunchRequest launchRequest) {
        DebuggerLoggingService.LogMessage($"Launching program: {launchRequest.Program} {string.Join(' ', launchRequest.Arguments)}");
        EnsureNotStarted();
        if (launchRequest.Console == ConsoleType.InternalConsole)
            await LaunchProcessAsync(launchRequest);
        else
            await LaunchInTerminalAsync(launchRequest);
    }
    public async Task AttachAsync(int processId) {
        EnsureNotStarted();
        await AttachToProcessAsync(processId, launchRequest: null);
    }
    // The transport is set up first, 'onListenerReady' then lets the host launch the on-device app so it can connect back,
    // and the attach is initiated last
    public void AttachRemote(RemoteAttachInfo attachInfo, Action? onListenerReady = null) {
        DebuggerLoggingService.LogMessage($"Attaching to remote target on {attachInfo.Address}:{attachInfo.Port} ({attachInfo.Platform})");
        EnsureNotStarted();
        isRemoteAttach = true;
        corDebug = DbgShimHost.CreateRemote(attachInfo);
        corDebug.SetManagedHandler(callbacks);
        onListenerReady?.Invoke();
        try {
            // No ICorDebugProcess comes back here, it arrives through the CreateProcess callback instead
            corDebug.DebugActiveProcess(0, false);
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogMessage($"DebugActiveProcess(0) threw as expected for a remote attach: {ex.Message}");
        }
        DebuggerLoggingService.LogMessage($"Debugger listening on port {attachInfo.Port}, awaiting the connection from the debuggee");
    }

    public void Continue() {
        ArgumentNullException.ThrowIfNull(process);
        isExceptionStopPending = false;
        ClearReferences();
        var result = process.TryContinue(false);
        if (result == Cor.CORDBG_E_SUPERFLOUS_CONTINUE)
            return;
        Marshal.ThrowExceptionForHR(result);
    }
    // Refused when the process cannot be stopped rather than skipped: the caller is told the program stopped, so it has to have stopped.
    // For a short while after an attach has landed ICorDebug reports the process as not running while the debuggee is plainly still
    // executing - it is working through the synthetic attach events. The state clears in a moment, so a client that wants the pause can ask again
    public void Pause(int threadId) {
        ArgumentNullException.ThrowIfNull(process);
        if (!IsRunning)
            throw new InvalidOperationException("The program is not running, so it cannot be paused: it has either stopped already or has not finished starting");

        process.Stop(0);
        stepController.Disable();
        // Stopping the process raises no callback, the stop is reported here
        OnStopped?.Invoke(new StopInfo(GetPausedThreadId(threadId), StopReason.Pause));
    }
    // The thread a pause is reported on: the one asked for when the client can show it. A thread without managed frames
    // is not listed (an idle Android main thread sits in Java), so another one with frames stands in for it
    private int GetPausedThreadId(int requestedThreadId) {
        try {
            if (threads.TryGetValue(requestedThreadId, out var requested) && requested.HasManagedFrames())
                return requestedThreadId;
            foreach (var (threadId, thread) in threads) {
                if (thread.HasManagedFrames())
                    return threadId;
            }
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError("Error choosing the paused thread", ex);
        }
        return threads.ContainsKey(requestedThreadId) ? requestedThreadId : threads.Keys.FirstOrDefault(requestedThreadId);
    }
    // A line of console input collected by the client while the debuggee runs, written to its standard input
    public bool WriteStandardInput(string text) {
        if (standardInput == null)
            return false;
        try {
            standardInput.WriteLine(text);
            standardInput.Flush();
            return true;
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogMessage($"Failed to write to the debuggee's standard input: {ex.Message}");
            return false;
        }
    }
    public async Task StepAsync(int threadId, StepKind kind) {
        DebuggerLoggingService.LogMessage($"Step {kind} on thread {threadId}");
        var thread = GetThread(threadId);
        await stepController.StepAsync(thread, kind);
        ClearReferences();
        ContinueProcess();
    }
    public void Terminate() {
        DebuggerLoggingService.LogMessage("Terminate");
        if (process != null) {
            try {
                StopBeforeShutdown("terminating");
                process.Terminate(0);
            }
            catch (Exception ex) {
                DebuggerLoggingService.LogError("Error terminating the process", ex);
            }
        }
        Dispose(killLaunchedProcess: true);
    }
    public void Disconnect(bool terminateDebuggee) {
        DebuggerLoggingService.LogMessage($"Disconnect (terminate: {terminateDebuggee})");
        if (terminateDebuggee) {
            Terminate();
            return;
        }
        StopBeforeShutdown("detaching");
        Dispose(killLaunchedProcess: false);
    }
    // Terminate and Detach need the process synchronized, on a running one they fail with CORDBG_E_PROCESS_NOT_SYNCHRONIZED
    private void StopBeforeShutdown(string action) {
        if (process == null || !IsRunning)
            return;
        var result = process.TryStop(0);
        if (result != Cor.S_OK && result != Cor.CORDBG_E_PROCESS_TERMINATED)
            DebuggerLoggingService.LogMessage($"Error stopping the process before {action}: 0x{result:X8}");
    }

    public List<Breakpoint> SetBreakpoints(string filePath, List<BreakpointRequest> requests) {
        DebuggerLoggingService.LogMessage($"SetBreakpoints: {filePath}, lines: {string.Join(", ", requests.Select(it => it.Line))}");
        return breakpointManager.SetBreakpoints(filePath, requests, Modules, RequireExactSource);
    }
    public List<Breakpoint> SetFunctionBreakpoints(List<FunctionBreakpointRequest> requests) {
        DebuggerLoggingService.LogMessage($"SetFunctionBreakpoints: {string.Join(", ", requests.Select(it => it.Name))}");
        return breakpointManager.SetFunctionBreakpoints(requests, Modules);
    }

    // The threads announced through 'CreateThread' that run managed code: the runtime's own (the finalizer, the tiered
    // compilation worker) have no managed frames while idle and a client could show nothing for them
    public List<ThreadInfo> GetThreads() {
        var result = new List<ThreadInfo>();
        if (process == null)
            return result;
        try {
            foreach (var (threadId, thread) in threads) {
                if (!thread.HasManagedFrames())
                    continue;
                result.Add(new ThreadInfo(threadId, thread.GetManagedName(), threadId == mainThreadId));
            }
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError("Error getting threads", ex);
        }
        return result;
    }
    public List<StackFrameInfo> GetStackFrames(int threadId) {
        var result = new List<StackFrameInfo>();
        var thread = threads.GetValueOrDefault(threadId);
        if (thread == null)
            return result;

        var depth = 0;
        foreach (var frame in thread.GetManagedFrames()) {
            var frameId = frameReferenceManager.GetOrCreate(threadId, depth++);
            result.Add(CreateStackFrameInfo(frameId, frame));
        }
        return result;
    }
    // The variables reference of the frame's locals, zero when the frame has nothing to show
    public int GetLocalsReference(int frameId) {
        var reference = frameReferenceManager.Get(frameId);
        if (reference == null || GetFrame(reference.ThreadId, reference.Depth) is not ICorDebugILFrame frame)
            return 0;
        if (frame.GetLocalVariables().Length == 0 && frame.GetArguments().Length == 0 && GetCurrentException(reference.ThreadId) == null)
            return 0;
        return variableProvider.CreateScopeReference(reference.ThreadId, reference.Depth);
    }
    // One page of the listing, starting at 'start' and holding at most 'count' variables
    public Task<VariablePage> GetVariablesAsync(int variablesReference, int start, int count) {
        return variableProvider.GetVariablesAsync(variablesReference, start, count);
    }
    // A variable, a field or an element takes a primitive value or 'null', a property any expression its setter is invoked with
    public Task<VariableInfo> SetVariableAsync(int variablesReference, string name, string value) {
        return variableProvider.SetVariableAsync(variablesReference, name, value);
    }
    public async Task<VariableInfo> EvaluateAsync(string expression, int frameId) {
        DebuggerLoggingService.LogMessage($"Evaluate: {expression}");
        var reference = frameReferenceManager.Get(frameId) ?? throw new InvalidOperationException("The frame id does not exist");
        var context = new EvaluationContext(GetThread(reference.ThreadId), reference.ThreadId, reference.Depth);
        using var result = await GetEvaluator().EvaluateAsync(expression, context);
        if (result.Error != null)
            throw new EvaluationException(result.Error);

        var variable = await variableProvider.CreateVariableAsync(expression, result.Value!, reference.ThreadId, reference.Depth, expression);
        // A value with children stays alive behind its variables reference
        if (variable.VariablesReference != 0)
            result.KeepHandle();
        return variable;
    }
    public async Task<ExceptionInfo> GetExceptionInfoAsync(int threadId) {
        var exception = GetCurrentException(threadId) ?? throw new InvalidOperationException("No current exception on the thread");
        var kind = exceptionStopKinds.GetValueOrDefault(threadId, ExceptionStopKind.FirstChance);
        // The frames of the raise are gone by the time of the stop, the module was captured back then
        var moduleName = exceptionModules.GetValueOrDefault(threadId);
        var details = await ReadExceptionDetailsAsync(exception, threadId);
        var innerException = await GetInnerExceptionAsync(exception, threadId);
        return new ExceptionInfo(details.TypeName, details.Message, details.StackTrace, kind, moduleName, innerException);
    }
    // The frame dependent parts (type name, recorded trace) are read before the property evaluation, which neuters the frames
    private async Task<InnerExceptionInfo> ReadExceptionDetailsAsync(ICorDebugValue exception, int threadId) {
        var typeName = ValueFormatter.Format(exception, false).TypeName;
        var stackTrace = GetExceptionStackTrace(exception);
        var message = await GetExceptionPropertyAsync(exception, threadId, "Message") ?? string.Empty;
        return new InnerExceptionInfo(typeName, message, stackTrace);
    }
    // The exception wrapped by the reported one, null without one. An AggregateException contributes its first inner, the property's value
    private async Task<InnerExceptionInfo?> GetInnerExceptionAsync(ICorDebugValue exception, int threadId) {
        var inner = await FuncEval.GetPropertyValueAsync(exception, GetThread(threadId), "InnerException");
        try {
            if (inner == null || (inner is ICorDebugReferenceValue reference && reference.IsNull()))
                return null;
            return await ReadExceptionDetailsAsync(inner, threadId);
        }
        finally {
            if (inner is ICorDebugHandleValue handle)
                handle.TryDispose();
        }
    }
    private async Task<string?> GetExceptionPropertyAsync(ICorDebugValue exception, int threadId, string propertyName) {
        var value = await FuncEval.GetPropertyValueAsync(exception, GetThread(threadId), propertyName) ?? throw new InvalidOperationException($"The exception property '{propertyName}' returned no value");
        try {
            if (value is ICorDebugReferenceValue reference && reference.IsNull())
                return null;
            var display = await variableProvider.FormatValueAsync(value, threadId, 0, escapeStrings: false, createProxy: false);
            return display.Value;
        }
        finally {
            if (value is ICorDebugHandleValue handle)
                handle.TryDispose();
        }
    }
    // Moves the instruction pointer of the thread's active frame to the given line ('Set Next Statement')
    public void SetNextStatement(int threadId, string filePath, int line) {
        var thread = GetThread(threadId);
        if (thread.GetActiveFrame() is not ICorDebugILFrame frame)
            throw new InvalidOperationException("The active frame is not an IL frame");

        var function = frame.GetFunction();
        var module = GetModule(function.GetModule());
        var resolved = module.MetadataReader.ResolveBreakpoint(filePath, line, null, RequireExactSource, out _)
            ?? throw new InvalidOperationException($"No executable code found at {Path.GetFileName(filePath)}:{line}");
        if (resolved.MethodToken != function.GetToken())
            throw new InvalidOperationException("The next statement must be within the current method");

        try {
            frame.SetIP(resolved.ILOffset);
        }
        catch (Exception ex) {
            throw new InvalidOperationException($"Cannot set the next statement: {ex.Message}");
        }
        // Frames and values are neutered by SetIP
        ClearReferences();
    }

    // Threads come from the 'CreateThread' callbacks rather than 'ICorDebugProcess.GetThread', which the remote (mobile) transport does not implement
    internal ICorDebugThread GetThread(int threadId) {
        return threads.GetValueOrDefault(threadId) ?? throw new InvalidOperationException($"Thread '{threadId}' not found");
    }
    // Frames are re-obtained from the thread every time, the ICorDebugFrame objects are neutered by any continue
    internal ICorDebugFrame GetFrame(int threadId, int depth) {
        return GetThread(threadId).GetManagedFrames().ElementAt(depth);
    }
    internal ICorDebugILFrame GetILFrame(int threadId, int depth) {
        if (GetFrame(threadId, depth) is not ICorDebugILFrame frame)
            throw new InvalidOperationException("The frame is not an IL frame");
        return frame;
    }
    // A module is known once its metadata could be read, which is what everything asking for it needs
    internal ModuleInfo GetModule(ICorDebugModule module) {
        return FindModule(module) ?? throw new InvalidOperationException($"The metadata of the module '{module.GetName()}' is not available");
    }
    internal ModuleInfo? FindModule(ICorDebugModule module) {
        return modules.GetValueOrDefault(module);
    }
    internal ICorDebugValue? GetCurrentException(int threadId) {
        var thread = threads.GetValueOrDefault(threadId);
        if (thread == null)
            return null;
        thread.TryGetCurrentException(out var exception);
        return exception;
    }
    internal SourceLocation? GetSourceLocation(ICorDebugFrame? frame) {
        if (frame is not ICorDebugILFrame ilFrame)
            return null;
        var function = ilFrame.GetFunction();
        var module = FindModule(function.GetModule());
        if (module == null || !module.HasSymbols)
            return null;
        return module.MetadataReader.GetSourceLocation(function.GetToken(), ilFrame.GetIP().pnOffset);
    }
    internal ExpressionEvaluator GetEvaluator() {
        return evaluator ?? throw new InvalidOperationException("Expressions cannot be evaluated before the runtime has loaded");
    }
    // Dispatches the callbacks arriving while a func eval runs, until its completion callback
    internal async Task<CorDebugManagedCallbackEventArgs> WaitForEvalEventAsync() {
        var reader = eventQueue.Reader;
        while (await reader.WaitToReadAsync()) {
            if (!reader.TryRead(out var callbackEvent))
                continue;
            await DispatchEventAsync(callbackEvent);
            if (callbackEvent is EvalCompleteCorDebugManagedCallbackEventArgs or EvalExceptionCorDebugManagedCallbackEventArgs)
                return callbackEvent;
        }
        throw new EvaluationException("The debugger stopped processing runtime events before the evaluation completed");
    }

    private void QueueEvent(object? sender, CorDebugManagedCallbackEventArgs callbackEvent) {
        eventQueue.Writer.TryWrite(callbackEvent);
    }
    private async Task ProcessEventQueueAsync() {
        var reader = eventQueue.Reader;
        try {
            while (await reader.WaitToReadAsync()) {
                await syncLock.WaitAsync();
                try {
                    // A request may have drained the queue while this waited for the lock
                    if (reader.TryRead(out var callbackEvent))
                        await DispatchEventAsync(callbackEvent);
                }
                finally {
                    syncLock.Release();
                }
            }
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError("Critical failure processing the runtime event queue, no further events will be processed", ex);
        }
    }
    private async Task DispatchEventAsync(CorDebugManagedCallbackEventArgs callbackEvent) {
        try {
            DebuggerLoggingService.LogMessage($"Event: {callbackEvent.Describe()}");
            switch (callbackEvent) {
                case LogMessageCorDebugManagedCallbackEventArgs logMessage:
                    HandleLogMessage(logMessage);
                    break;
                case CreateProcessCorDebugManagedCallbackEventArgs processCreated:
                    HandleProcessCreated(processCreated);
                    break;
                case ExitProcessCorDebugManagedCallbackEventArgs processExited:
                    HandleProcessExited(processExited);
                    break;
                case CreateThreadCorDebugManagedCallbackEventArgs threadCreated:
                    HandleThreadCreated(threadCreated);
                    break;
                case ExitThreadCorDebugManagedCallbackEventArgs threadExited:
                    HandleThreadExited(threadExited);
                    break;
                case LoadModuleCorDebugManagedCallbackEventArgs moduleLoaded:
                    HandleModuleLoaded(moduleLoaded);
                    break;
                case LoadClassCorDebugManagedCallbackEventArgs classLoaded:
                    HandleClassLoaded(classLoaded);
                    break;
                case BreakpointCorDebugManagedCallbackEventArgs breakpoint:
                    await HandleBreakpointAsync(breakpoint);
                    break;
                case StepCompleteCorDebugManagedCallbackEventArgs stepComplete:
                    HandleStepComplete(stepComplete);
                    break;
                case BreakCorDebugManagedCallbackEventArgs breakEvent:
                    HandleBreak(breakEvent);
                    break;
                case ExceptionCorDebugManagedCallbackEventArgs exception:
                    HandleException(exception);
                    break;
                case Exception2CorDebugManagedCallbackEventArgs exception:
                    HandleExceptionDispatch(exception);
                    break;
                case EvalCompleteCorDebugManagedCallbackEventArgs:
                case EvalExceptionCorDebugManagedCallbackEventArgs:
                    // The evaluation that started it is waiting for it and continues on its own
                    break;
                default:
                    ContinueProcess();
                    break;
            }
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError($"Error handling {callbackEvent.GetType().Name}", ex);
            // A failing recovery continue is logged rather than thrown: an exception here would end the event loop
            if (process != null && process.TryIsRunning(out var isRunning) == Cor.S_OK && !isRunning) {
                var result = process.TryContinue(false);
                if (result != Cor.S_OK)
                    DebuggerLoggingService.LogMessage($"The process could not be continued after the failed handler: 0x{result:X8}");
            }
        }
    }

    private async Task LaunchProcessAsync(LaunchRequest launchRequest) {
        var startInfo = new ProcessStartInfo {
            FileName = launchRequest.Program,
            WorkingDirectory = launchRequest.WorkingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (var argument in launchRequest.Arguments)
            startInfo.ArgumentList.Add(argument);
        foreach (var (key, value) in launchRequest.Environment)
            startInfo.Environment[key] = value;
        // The runtime waits for a diagnostics client before starting, so the attach lands before any managed code runs.
        // Set last, whatever the configuration says; it is taken back out of the debuggee's environment before the
        // runtime is resumed, so the processes the debuggee starts do not inherit it (see 'ResumeLaunchedRuntimeAsync')
        startInfo.Environment[DiagnosticPortSuspendVariable] = "1";

        var started = Process.Start(startInfo) ?? throw new InvalidOperationException("The process could not be started");
        launchedProcess = started;
        standardInput = started.StandardInput;
        _ = Task.Run(() => PumpOutputAsync(started.StandardOutput, isError: false));
        _ = Task.Run(() => PumpOutputAsync(started.StandardError, isError: true));
        DebuggerLoggingService.LogMessage($"Process created suspended with PID: {started.Id}");

        stopAtEntryPending = launchRequest.StopAtEntry;
        await AttachToProcessAsync(started.Id, launchRequest);
        OnProcessStarted?.Invoke(started.Id);
    }
    private async Task LaunchInTerminalAsync(LaunchRequest launchRequest) {
        var handler = OnTerminalLaunchRequested ?? throw new InvalidOperationException("Launching in a terminal requires an 'OnTerminalLaunchRequested' subscriber");
        // The handler blocks on the client's response, which must not happen on the thread dispatching requests
        await Task.Run(() => handler.Invoke(launchRequest));
        var processId = launchRequest.ProcessId ?? throw new InvalidOperationException("The terminal launch did not provide the id of the started process");
        stopAtEntryPending = launchRequest.StopAtEntry;
        await AttachToProcessAsync(processId, launchRequest);
        OnProcessStarted?.Invoke(processId);
    }
    // 'launchRequest' is the launch that parked the debuggee with DOTNET_DefaultDiagnosticPortSuspend, null for an attach.
    // A launched runtime has to be resumed, and the variable is taken back out of its environment first. An attach target
    // may have been started that way by someone else, so the resume is attempted and its failure ignored: a running
    // runtime refuses it, and its environment is its own
    private async Task AttachToProcessAsync(int processId, LaunchRequest? launchRequest) {
        DebuggerLoggingService.LogMessage($"Attaching to process: {processId}");
        // The registration is made before the runtime is resumed, so the startup notification is not missed
        using var attachCancellation = new CancellationTokenSource();
        var attachTask = DbgShimHost.AttachAsync(processId, target => AttachToRuntime(target, processId), attachCancellation.Token);
        try {
            if (launchRequest == null) {
                await DiagnosticsClientHelper.ResumeRuntimeAsync(processId);
            }
            else {
                launchRequest.Environment.TryGetValue(DiagnosticPortSuspendVariable, out var configuredValue);
                await DiagnosticsClientHelper.ResumeLaunchedRuntimeAsync(processId, configuredValue);
            }
        }
        catch (Exception ex) when (launchRequest == null) {
            DebuggerLoggingService.LogMessage($"Failed to resume the runtime of the attach target (already running?): {ex.Message}");
        }
        catch {
            // The runtime stays parked and its startup never comes: the registration is withdrawn, or it would
            // refuse every later attach made from this process
            attachCancellation.Cancel();
            try {
                await attachTask;
            }
            catch (Exception attachException) {
                DebuggerLoggingService.LogMessage($"The attach was withdrawn: {attachException.Message}");
            }
            throw;
        }
        await attachTask;
        DebuggerLoggingService.LogMessage($"Attached to process: {processId}");
        ProcessId = processId;
    }
    // Runs inside dbgshim's runtime startup callback, while the debuggee's runtime is still parked in its startup handshake
    private void AttachToRuntime(ICorDebug target, int processId) {
        target.Initialize();
        target.SetManagedHandler(callbacks);
        corDebug = target;
        process = target.DebugActiveProcess(processId, false);
    }
    // A debugger drives one debuggee: a second start would replace the native objects of the first
    private void EnsureNotStarted() {
        if (corDebug != null)
            throw new InvalidOperationException("The debugger is already attached to a debuggee");
    }
    // The debuggee's output is forwarded in raw chunks rather than lines, so an unterminated prompt such as
    // 'Enter name: ' reaches the client before the debuggee blocks on reading the answer. The pump runs on a
    // background thread: whatever it throws is logged here rather than taking the adapter down
    private async Task PumpOutputAsync(StreamReader reader, bool isError) {
        var buffer = new char[4096];
        try {
            while (true) {
                var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (read <= 0)
                    return;
                OnOutput?.Invoke(new string(buffer, 0, read), isError);
            }
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogMessage($"Stopped reading the debuggee output: {ex.Message}");
        }
    }

    private void ContinueProcess() {
        ArgumentNullException.ThrowIfNull(process);
        process.Continue(false);
    }
    // Variables and frames are only valid until the debuggee runs again
    private void ClearReferences() {
        variableManager.Clear();
        frameReferenceManager.Clear();
    }
    private void Dispose(bool killLaunchedProcess) {
        foreach (var module in modules.Values)
            module.Dispose();
        modules.Clear();

        breakpointManager.Clear();
        ClearEntryPointBreakpoint();
        stopAtEntryPending = false;
        exceptionThreads.Clear();
        exceptionStopKinds.Clear();
        exceptionModules.Clear();
        exceptionAddresses.Clear();
        stepController.Disable();
        threads.Clear();
        ClearReferences();

        // No further callbacks are dispatched. The event loop waits for the lock held here, so the queue is
        // drained by hand and the loop exits once the lock is released. The queue is already completed
        // when the debuggee exited on its own
        callbacks.OnAnyEvent -= QueueEvent;
        eventQueue.Writer.TryComplete();
        while (eventQueue.Reader.TryRead(out _)) { }

        process?.TryDetach();
        process = null;
        corDebug = null;
        evaluator = null;

        if (killLaunchedProcess)
            launchedProcess?.Kill();
        launchedProcess?.Dispose();
        launchedProcess = null;
        standardInput = null;
    }

    private StackFrameInfo CreateStackFrameInfo(int frameId, ICorDebugFrame frame) {
        if (frame is ICorDebugILFrame ilFrame) {
            var function = ilFrame.GetFunction();
            var module = GetModule(function.GetModule());
            var info = new StackFrameInfo(frameId, StackFrameKind.Managed, function.GetDisplayName(module.MetadataReader.PeMetadataReader));
            info.ModuleName = module.Name;
            info.ModuleId = module.Id;
            info.Location = GetSourceLocation(ilFrame);
            return info;
        }
        if (frame is ICorDebugInternalFrame internalFrame)
            return new StackFrameInfo(frameId, StackFrameKind.Internal, internalFrame.GetDisplayName());
        return new StackFrameInfo(frameId, StackFrameKind.Native, "[Native Frame]");
    }
}
