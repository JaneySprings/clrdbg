using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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
using DotNet.Debugging.Engine.Metadata;
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
    private readonly Dictionary<CordbAddress, ModuleInfo> modules;
    private readonly Dictionary<int, ICorDebugThread> threads;
    // Threads whose current exception was thrown in, or passed through, user code
    private readonly HashSet<int> exceptionThreads;
    private readonly Dictionary<int, ExceptionStopKind> exceptionStopKinds;
    // The module each thread's current exception is attributed to, captured at the raise: the stop
    // happens later in the dispatch, when the thread's frames no longer show it
    private readonly Dictionary<int, string?> exceptionModules;
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
    private LaunchInfo? pendingLaunch;
    private int? pendingAttachProcessId;
    private RemoteAttachInfo? pendingRemoteAttach;
    private Action? onRemoteListenerReady;
    private ICorDebugFunctionBreakpoint? entryPointBreakpoint;
    private bool stopAtEntryPending;
    private bool isRemoteAttach;
    private int? mainThreadId;

    public bool JustMyCode { get; set; } = true;
    // A source file matched by name alone (PDBs built from a different location) must also match the PDB's content checksum
    public bool RequireExactSource { get; set; } = true;
    // 'Step over properties and operators': a step never stops inside an accessor or an operator method
    public bool EnableStepFiltering { get; set; } = true;
    // Starts the debuggee in the client's terminal and returns its process id, for launches with a terminal console
    public Func<LaunchInfo, int>? RunInTerminalHandler { get; set; }
    // Whether ICorDebug reports the debuggee as executing. A state that cannot be read counts as not running
    public bool IsRunning => process != null && process.TryIsRunning(out var isRunning) == Cor.S_OK && isRunning;
    // Whether the debuggee's standard input is held open for writing, which only an internal-console launch is
    public bool HasStandardInput => standardInput != null;
    public int ProcessId { get; private set; }

    internal FuncEvalRunner FuncEval { get; }
    internal IReadOnlyCollection<ModuleInfo> Modules => modules.Values;
    // Incremented whenever a module is loaded, so everything derived from the module set can detect staleness
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
    // Output text of a launched debuggee, 'true' for stderr
    public event Action<string, bool>? OnOutput;
    public event Action<string>? OnLogPoint;
    public event Action<Breakpoint>? OnBreakpointChanged;

    public ManagedDebugger() {
        callbacks = new CorDebugManagedCallback();
        eventQueue = Channel.CreateUnbounded<CorDebugManagedCallbackEventArgs>(new UnboundedChannelOptions { SingleWriter = true });
        syncLock = new SemaphoreSlim(1, 1);
        modules = new Dictionary<CordbAddress, ModuleInfo>();
        threads = new Dictionary<int, ICorDebugThread>();
        exceptionThreads = new HashSet<int>();
        exceptionStopKinds = new Dictionary<int, ExceptionStopKind>();
        exceptionModules = new Dictionary<int, string?>();
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

    // The launch, attach and remote attach are deferred until 'ConfigurationDoneAsync', so the breakpoints are known by then
    public void Launch(LaunchInfo launchInfo) {
        DebuggerLoggingService.LogMessage($"Launching program: {launchInfo.Program} {string.Join(' ', launchInfo.Arguments)}");
        pendingLaunch = launchInfo;
    }
    public void Attach(int processId) {
        DebuggerLoggingService.LogMessage($"Storing attach target: {processId}");
        pendingAttachProcessId = processId;
    }
    // 'onListenerReady' is invoked once the remote transport is listening, which is when the on-device app should be launched so it can connect back
    public void AttachRemote(RemoteAttachInfo attachInfo, Action? onListenerReady = null) {
        DebuggerLoggingService.LogMessage($"Storing remote attach target: {attachInfo.Address}:{attachInfo.Port}");
        isRemoteAttach = true;
        pendingRemoteAttach = attachInfo;
        onRemoteListenerReady = onListenerReady;
    }
    public async Task ConfigurationDoneAsync() {
        DebuggerLoggingService.LogMessage("ConfigurationDone");
        if (pendingLaunch != null && pendingLaunch.Console != ConsoleType.InternalConsole) {
            var launchInfo = pendingLaunch;
            pendingLaunch = null;
            await LaunchInTerminalAsync(launchInfo);
        }
        else if (pendingLaunch != null) {
            var launchInfo = pendingLaunch;
            pendingLaunch = null;
            await LaunchProcessAsync(launchInfo);
        }
        else if (pendingRemoteAttach != null) {
            var attachInfo = pendingRemoteAttach;
            pendingRemoteAttach = null;
            AttachToRemote(attachInfo);
        }
        else if (pendingAttachProcessId != null) {
            var processId = pendingAttachProcessId.Value;
            pendingAttachProcessId = null;
            // The target may have been started with DOTNET_DefaultDiagnosticPortSuspend, a running one refuses the resume
            await AttachAsync(processId, resumeRuntime: true, ignoreResumeFailure: true);
        }
    }

    public void Continue() {
        ArgumentNullException.ThrowIfNull(process);
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
        var stoppedThreadId = threads.ContainsKey(threadId) ? threadId : threads.Keys.FirstOrDefault(threadId);
        OnStopped?.Invoke(new StopInfo(stoppedThreadId, StopReason.Pause));
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
                // Terminate needs the process synchronized, on a running one it fails with CORDBG_E_PROCESS_NOT_SYNCHRONIZED
                if (IsRunning) {
                    var result = process.TryStop(0);
                    if (result != Cor.S_OK && result != Cor.CORDBG_E_PROCESS_TERMINATED)
                        DebuggerLoggingService.LogMessage($"Error stopping the process before terminating: 0x{result:X8}");
                }
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
        if (process != null && IsRunning) {
            var result = process.TryStop(0);
            if (result != Cor.S_OK && result != Cor.CORDBG_E_PROCESS_TERMINATED)
                DebuggerLoggingService.LogMessage($"Error stopping the process before detaching: 0x{result:X8}");
        }
        Dispose(killLaunchedProcess: false);
    }

    public List<Breakpoint> SetBreakpoints(string filePath, List<BreakpointRequest> requests) {
        DebuggerLoggingService.LogMessage($"SetBreakpoints: {filePath}, lines: {string.Join(", ", requests.Select(it => it.Line))}");
        return breakpointManager.SetBreakpoints(filePath, requests, Modules, process != null, RequireExactSource);
    }
    public List<Breakpoint> SetFunctionBreakpoints(List<FunctionBreakpointRequest> requests) {
        DebuggerLoggingService.LogMessage($"SetFunctionBreakpoints: {string.Join(", ", requests.Select(it => it.Name))}");
        return breakpointManager.SetFunctionBreakpoints(requests, Modules, process != null);
    }

    public List<ThreadInfo> GetThreads() {
        var result = new List<ThreadInfo>();
        if (process == null)
            return result;
        try {
            foreach (var thread in process.EnumerateThreads()) {
                var threadId = thread.GetId();
                var isMain = threadId == mainThreadId;
                // The OS name of the main thread is the executable's ('dotnet' on Linux), the host labels it instead
                var name = GetManagedThreadName(thread);
                if (name == null && !isMain)
                    name = NativeThreadNames.GetThreadName(ProcessId, threadId);
                result.Add(new ThreadInfo(threadId, name, isMain));
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
        foreach (var frame in EnumerateFrames(thread)) {
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
    // Only primitive values and 'null' for references can be assigned
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
        var typeName = ValueFormatter.Format(exception, false).TypeName;
        var kind = exceptionStopKinds.GetValueOrDefault(threadId, ExceptionStopKind.FirstChance);
        // The frames of the raise are gone by the time of the stop, the module was captured back then
        var moduleName = exceptionModules.GetValueOrDefault(threadId);
        // The frame dependent parts are read before the property evaluations, which neuter the frames
        var stackTrace = GetExceptionStackTrace(exception);
        var message = await GetExceptionPropertyAsync(exception, threadId, "Message") ?? string.Empty;
        var source = await GetExceptionPropertyAsync(exception, threadId, "Source");
        var hresult = int.Parse(await GetExceptionPropertyAsync(exception, threadId, "HResult") ?? "0");
        var innerExceptionChain = await GetInnerExceptionChainAsync(exception, threadId);
        return new ExceptionInfo(typeName, message, source, stackTrace, hresult, kind, moduleName, innerExceptionChain);
    }
    // The whole 'InnerException' chain, the direct inner first - Microsoft's debugger nests them in 'innerException',
    // shows the innermost exception's recorded trace in place of the reported one and names it in the
    // description of the stop. An AggregateException contributes its first inner, the property's value
    private async Task<List<InnerExceptionInfo>> GetInnerExceptionChainAsync(ICorDebugValue exception, int threadId) {
        var chain = new List<InnerExceptionInfo>();
        var handles = new List<ICorDebugHandleValue>();
        try {
            var current = exception;
            // The depth guard breaks out of a cyclic chain
            for (var depth = 0; depth < 32; depth++) {
                var frame = GetILFrame(threadId, 0);
                var inner = await FuncEval.GetPropertyValueAsync(current, frame, "InnerException");
                if (inner == null || (inner is ICorDebugReferenceValue reference && reference.IsNull())) {
                    if (inner is ICorDebugHandleValue nullHandle)
                        nullHandle.TryDispose();
                    break;
                }
                if (inner is ICorDebugHandleValue handle)
                    handles.Add(handle);

                var typeName = ValueFormatter.Format(inner, false).TypeName;
                var stackTrace = GetExceptionStackTrace(inner);
                var message = await GetExceptionPropertyAsync(inner, threadId, "Message") ?? string.Empty;
                var source = await GetExceptionPropertyAsync(inner, threadId, "Source");
                var hresult = int.Parse(await GetExceptionPropertyAsync(inner, threadId, "HResult") ?? "0");
                chain.Add(new InnerExceptionInfo(typeName, message, source, stackTrace, hresult));
                current = inner;
            }
            return chain;
        }
        finally {
            foreach (var handle in handles)
                handle.TryDispose();
        }
    }
    private async Task<string?> GetExceptionPropertyAsync(ICorDebugValue exception, int threadId, string propertyName) {
        // Every func eval neuters the frames, so the frame is re-obtained for each property
        var frame = GetILFrame(threadId, 0);
        var value = await FuncEval.GetPropertyValueAsync(exception, frame, propertyName) ?? throw new InvalidOperationException($"The exception property '{propertyName}' returned no value");
        try {
            if (value is ICorDebugReferenceValue reference && reference.IsNull())
                return null;
            var display = await variableProvider.FormatValueAsync(value, threadId, 0, false);
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
        return EnumerateFrames(GetThread(threadId)).ElementAt(depth);
    }
    internal ICorDebugILFrame GetILFrame(int threadId, int depth) {
        if (GetFrame(threadId, depth) is not ICorDebugILFrame frame)
            throw new InvalidOperationException("The frame is not an IL frame");
        return frame;
    }
    internal ModuleInfo GetModule(ICorDebugModule module) {
        return modules[module.GetBaseAddress()];
    }
    internal ModuleInfo? FindModule(ICorDebugModule module) {
        return modules.GetValueOrDefault(module.GetBaseAddress());
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
            DebuggerLoggingService.LogMessage($"Event: {callbackEvent.GetType().Name}");
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
            if (process != null && process.TryIsRunning(out var isRunning) == Cor.S_OK && !isRunning)
                ContinueProcess();
        }
    }

    private async Task LaunchProcessAsync(LaunchInfo launchInfo) {
        var startInfo = new ProcessStartInfo {
            FileName = launchInfo.Program,
            WorkingDirectory = launchInfo.WorkingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = launchInfo.Console == ConsoleType.InternalConsole,
        };
        foreach (var argument in launchInfo.Arguments)
            startInfo.ArgumentList.Add(argument);
        foreach (var (key, value) in launchInfo.Environment)
            startInfo.Environment[key] = value;
        // The runtime waits for a diagnostics client before starting, so the attach lands before any managed code runs
        startInfo.Environment[DiagnosticPortSuspendVariable] = "1";

        var started = Process.Start(startInfo) ?? throw new InvalidOperationException("The process could not be started");
        launchedProcess = started;
        if (launchInfo.Console == ConsoleType.InternalConsole)
            standardInput = started.StandardInput;
        _ = Task.Run(() => PumpOutputAsync(started.StandardOutput, isError: false));
        _ = Task.Run(() => PumpOutputAsync(started.StandardError, isError: true));
        DebuggerLoggingService.LogMessage($"Process created suspended with PID: {started.Id}");

        stopAtEntryPending = launchInfo.StopAtEntry;
        await AttachAsync(started.Id, resumeRuntime: true, ignoreResumeFailure: false);
        OnProcessStarted?.Invoke(started.Id);
    }
    private async Task LaunchInTerminalAsync(LaunchInfo launchInfo) {
        var handler = RunInTerminalHandler ?? throw new InvalidOperationException("Launching in a terminal requires a RunInTerminalHandler");
        // The handler blocks on the client's response, which must not happen on the thread dispatching requests
        var processId = await Task.Run(() => handler.Invoke(launchInfo));
        stopAtEntryPending = launchInfo.StopAtEntry;
        await AttachAsync(processId, resumeRuntime: true, ignoreResumeFailure: false);
        OnProcessStarted?.Invoke(processId);
    }
    private async Task AttachAsync(int processId, bool resumeRuntime, bool ignoreResumeFailure) {
        DebuggerLoggingService.LogMessage($"Attaching to process: {processId}");
        // The registration is made before the runtime is resumed, so the startup notification is not missed
        var attachTask = DbgShimHost.AttachAsync(processId, target => AttachToRuntime(target, processId));
        if (resumeRuntime) {
            try {
                await DiagnosticsClientHelper.ResumeRuntimeAsync(processId);
            }
            catch (Exception ex) when (ignoreResumeFailure) {
                DebuggerLoggingService.LogMessage($"Failed to resume the runtime of the attach target (already running?): {ex.Message}");
            }
        }
        await attachTask;
        DebuggerLoggingService.LogMessage($"Attached to process: {processId}");
        ProcessId = processId;
        SendBreakpointStatus();
    }
    // Runs inside dbgshim's runtime startup callback, while the debuggee's runtime is still parked in its startup handshake
    private void AttachToRuntime(ICorDebug target, int processId) {
        target.Initialize();
        target.SetManagedHandler(callbacks);
        corDebug = target;
        process = target.DebugActiveProcess(processId, false);
    }
    // The transport is set up first, the on-device app is launched to connect back and the attach is initiated last
    private void AttachToRemote(RemoteAttachInfo attachInfo) {
        DebuggerLoggingService.LogMessage($"Attaching to remote target on {attachInfo.Address}:{attachInfo.Port} ({attachInfo.Platform})");
        corDebug = DbgShimHost.CreateRemote(attachInfo);
        corDebug.SetManagedHandler(callbacks);
        onRemoteListenerReady?.Invoke();
        onRemoteListenerReady = null;
        try {
            // No ICorDebugProcess comes back here, it arrives through the CreateProcess callback instead
            corDebug.DebugActiveProcess(0, false);
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogMessage($"DebugActiveProcess(0) threw as expected for a remote attach: {ex.Message}");
        }
        DebuggerLoggingService.LogMessage($"Debugger listening on port {attachInfo.Port}, awaiting the connection from the debuggee");
        SendBreakpointStatus();
    }
    // A breakpoint event for every breakpoint, so the client shows the pending ones as unverified until they bind
    private void SendBreakpointStatus() {
        foreach (var breakpoint in breakpointManager.MarkProcessStarted())
            OnBreakpointChanged?.Invoke(breakpoint);
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

    private static IEnumerable<ICorDebugFrame> EnumerateFrames(ICorDebugThread thread) {
        foreach (var chain in thread.EnumerateChains()) {
            if (!chain.IsManaged())
                continue;
            foreach (var frame in chain.EnumerateFrames())
                yield return frame;
        }
    }
    private StackFrameInfo CreateStackFrameInfo(int frameId, ICorDebugFrame frame) {
        if (frame is ICorDebugILFrame ilFrame) {
            var function = ilFrame.GetFunction();
            var module = GetModule(function.GetModule());
            var info = new StackFrameInfo(frameId, StackFrameKind.Managed, GetMethodDisplayName(function, module));
            info.ModuleName = module.Name;
            info.ModulePath = module.Path;
            info.Location = GetSourceLocation(ilFrame);
            info.InstructionPointer = GetInstructionPointer(frame, function);
            return info;
        }
        if (frame is ICorDebugInternalFrame internalFrame)
            return new StackFrameInfo(frameId, StackFrameKind.Internal, GetInternalFrameName(internalFrame.GetFrameType()));
        return new StackFrameInfo(frameId, StackFrameKind.Native, "[Native Frame]");
    }
    // 'Namespace.Type.Method(string[] args)', the parameter list comes from the PE metadata
    private static string GetMethodDisplayName(ICorDebugFunction function, ModuleInfo module) {
        try {
            var token = function.GetToken();
            var metadataImport = function.GetModule().GetMetaDataInterface<IMetaDataImport>();
            var methodName = metadataImport.GetMethodProps(token).szMethod;
            var typeName = metadataImport.GetTypeDefProps(function.GetClass().GetToken()).szTypeDef;
            return $"{typeName}.{methodName}({GetParameterList(module.MetadataReader.PeMetadataReader, token, DisplayNameSignatureProvider.Instance)})";
        }
        catch {
            return "Unknown";
        }
    }
    private static string GetParameterList(MetadataReader reader, int methodToken, ISignatureTypeProvider<string, object?> typeProvider) {
        try {
            var method = reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(methodToken));
            var parameterTypes = method.DecodeSignature(typeProvider, null).ParameterTypes;
            // Sequence 0 is the return value, the rest are the 1-based positional parameters
            var parameterNames = method.GetParameters()
                .Select(reader.GetParameter)
                .Where(it => it.SequenceNumber > 0)
                .OrderBy(it => it.SequenceNumber)
                .Select(it => reader.GetString(it.Name))
                .ToList();
            var parameters = parameterTypes.Select((type, index) => index < parameterNames.Count ? $"{type} {parameterNames[index]}" : type);
            return string.Join(", ", parameters);
        }
        catch {
            return string.Empty;
        }
    }
    // The native address the frame is executing at: the start of the jitted code plus the native offset
    private static ulong? GetInstructionPointer(ICorDebugFrame frame, ICorDebugFunction function) {
        try {
            if (frame is not ICorDebugNativeFrame nativeFrame)
                return null;
            return function.GetNativeCode().GetAddress().Value + (ulong)nativeFrame.GetIP();
        }
        catch {
            // Not jitted yet, or no native view of the frame
            return null;
        }
    }
    private static string GetInternalFrameName(CorDebugInternalFrameType frameType) {
        return frameType switch {
            CorDebugInternalFrameType.STUBFRAME_M2U => "[Managed to Native Transition]",
            CorDebugInternalFrameType.STUBFRAME_U2M => "[Native to Managed Transition]",
            CorDebugInternalFrameType.STUBFRAME_APPDOMAIN_TRANSITION => "[Appdomain Transition]",
            CorDebugInternalFrameType.STUBFRAME_LIGHTWEIGHT_FUNCTION => "[Lightweight Function]",
            CorDebugInternalFrameType.STUBFRAME_FUNC_EVAL => "[Function Evaluation]",
            CorDebugInternalFrameType.STUBFRAME_INTERNALCALL => "[Internal Call]",
            CorDebugInternalFrameType.STUBFRAME_CLASS_INIT => "[Class Initialization]",
            CorDebugInternalFrameType.STUBFRAME_EXCEPTION => "[Exception]",
            CorDebugInternalFrameType.STUBFRAME_SECURITY => "[Security]",
            CorDebugInternalFrameType.STUBFRAME_JIT_COMPILATION => "[JIT Compilation]",
            _ => "[Unknown]"
        };
    }

    // The managed 'Thread.Name': the '_name' field of the Thread object, read without running code
    private string? GetManagedThreadName(ICorDebugThread thread) {
        try {
            if (thread.GetObject()?.UnwrapDebugValue() is ICorDebugObjectValue threadObject) {
                var corClass = threadObject.GetClass();
                var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
                var nameField = metadataImport.EnumFieldsWithName(corClass.GetToken(), "_name").SingleOrDefault();
                if (!nameField.IsNil && threadObject.GetFieldValue(corClass, nameField).UnwrapDebugValue() is ICorDebugStringValue name) {
                    var managedName = name.GetString();
                    if (!string.IsNullOrEmpty(managedName))
                        return managedName;
                }
            }
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogMessage($"Failed to read the managed name of thread {thread.GetId()}: {ex.Message}");
        }
        return null;
    }
}
