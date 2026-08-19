using System.Diagnostics;
using System.Runtime.InteropServices;
using DotNet.Debugging.Engine.ExpressionEvaluator;
using DotNet.Debugging.Engine.Models;
using DotNet.Debugging.Engine.Models.Response;
using DotNet.Debugging.Engine.PresentationHintModels;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Engine;

public record BreakpointRequest(int Line, string? Condition = null, string? HitCondition = null, int? Column = null);
public record FunctionBreakpointRequest(string Name, string? Condition = null, string? HitCondition = null);

public partial class ManagedDebugger {
    // Store launch info for deferred attach in ConfigurationDone
    private LaunchInfo? _pendingLaunchInfo;
    // Store remote (mobile) attach info for deferred attach in ConfigurationDone
    private RemoteAttachInfo? _pendingRemoteAttachInfo;
    private Action? _onRemoteListenerReady;

    /// <summary>
    /// Stores the launch request info for use in handling ConfigurationDone
    /// </summary>
    public void Launch(LaunchInfo launchInfo, bool justMyCode) {
        _logger?.Invoke($"Launching program: {launchInfo.Program} {string.Join(' ', launchInfo.Arguments)}");
        _justMyCode = justMyCode;
        _pendingLaunchInfo = launchInfo;
    }

    /// <summary>
    /// Actually perform the launch using DbgShim APIs
    /// </summary>
    private async Task PerformLaunch() {
        if (_pendingLaunchInfo is null) {
            _logger?.Invoke("No pending launch to perform");
            return;
        }

        var launchInfo = _pendingLaunchInfo;
        _pendingLaunchInfo = null;
        _stopAtEntryPending = launchInfo.StopAtEntry;

        var processStartInfo = new ProcessStartInfo {
            FileName = launchInfo.Program,
            WorkingDirectory = launchInfo.Cwd ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
        };
        foreach (var arg in launchInfo.Arguments) {
            processStartInfo.ArgumentList.Add(arg);
        }

        foreach (var (key, value) in launchInfo.Env) {
            processStartInfo.Environment[key] = value;
        }
        processStartInfo.Environment["DOTNET_DefaultDiagnosticPortSuspend"] = "1";

        var process = Process.Start(processStartInfo);
        if (process is null) throw new InvalidOperationException("Process start failed");
        _debuggeeProcess = process;

        process.OutputDataReceived += (_, e) => {
            if (e.Data is not null) InvokeOnOutputWithTryCatch(e.Data + Environment.NewLine, isError: false);
        };
        process.ErrorDataReceived += (_, e) => {
            if (e.Data is not null) InvokeOnOutputWithTryCatch(e.Data + Environment.NewLine, isError: true);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var processId = process.Id;

        _logger?.Invoke($"Process created suspended with PID: {processId}");

        _corDebug = await ClrDebugExtensions.Automatic(processId, true);
        _corDebug.Initialize();
        _corDebug.SetManagedHandler(_callbacks);

        _process = _corDebug.DebugActiveProcess(processId, false);
        _isAttached = true;

        _logger?.Invoke($"Successfully attached to process: {processId}");
        SendAllBreakpointEvents();
        return;

        // The DataReceived callbacks run on background threads - a throwing subscriber (e.g. a disposed protocol layer) must not crash the adapter
        void InvokeOnOutputWithTryCatch(string output, bool isError) {
            try {
                OnOutput?.Invoke(output, isError);
            }
            catch (Exception ex) {
                _logger?.Invoke($"Error forwarding debuggee output: {ex.Message}");
            }
        }
    }

    public bool RemoveBreakpoint(int id) {
        _logger?.Invoke($"RemoveBreakpoint: {id}");
        var bp = _breakpointManager.GetBreakpoint(id);
        if (bp is null) return false;
        if (bp.CorBreakpoint is not null) {
            try {
                bp.CorBreakpoint.Activate(false);
            }
            catch (Exception ex) {
                _logger?.Invoke($"Error deactivating breakpoint: {ex.Message}");
            }
        }
        return _breakpointManager.RemoveBreakpoint(id);
    }

    /// <summary>
    /// Store process ID for later attach (actual attach happens in ConfigurationDone)
    /// </summary>
    public void Attach(int processId, bool justMyCode) {
        _logger?.Invoke($"Storing attach target: {processId}");
        _justMyCode = justMyCode;
        _pendingAttachProcessId = processId;
    }

    /// <summary>
    /// Store remote (mobile/maccatalyst) attach info for use in handling ConfigurationDone. <paramref name="onListenerReady"/>
    /// is invoked once the debugger's remote transport is listening, which is when the on-device app should be launched
    /// so it can connect back to the debugger.
    /// </summary>
    public void AttachRemote(RemoteAttachInfo remoteAttachInfo, bool justMyCode, Action? onListenerReady = null) {
        _logger?.Invoke($"Storing remote attach target: {remoteAttachInfo.Address}:{remoteAttachInfo.Port}");
        _justMyCode = justMyCode;
        _isRemoteAttach = true;
        _pendingRemoteAttachInfo = remoteAttachInfo;
        _onRemoteListenerReady = onListenerReady;
    }

    /// <summary>
    /// Called when DAP configuration is complete - performs deferred launch or attach
    /// </summary>
    public async Task ConfigurationDone() {
        //System.Diagnostics.Debugger.Launch();
        _logger?.Invoke("ConfigurationDone");

        if (_pendingLaunchInfo is { LaunchRequestConsoleType: LaunchRequestConsoleType.ExternalTerminal or LaunchRequestConsoleType.IntegratedTerminal }) {
            var launchedProcessId = await Task.Run(() => SendRunInTerminalRequest.Invoke(_pendingLaunchInfo)); // get off the dispatcher thread
            _pendingLaunchInfo = null;
            PerformAttach(launchedProcessId);
            await DiagnosticClientHelper.DiagnosticClientResumeRuntime(launchedProcessId);
        }
        else if (_pendingRemoteAttachInfo is not null) // Remote (mobile/maccatalyst) attach
        {
            var remoteAttachInfo = _pendingRemoteAttachInfo;
            var onListenerReady = _onRemoteListenerReady;
            _pendingRemoteAttachInfo = null;
            _onRemoteListenerReady = null;
            PerformRemoteAttach(remoteAttachInfo, onListenerReady);
        }
        else if (_pendingLaunchInfo is not null) // If we have a pending launch, perform it
        {
            await PerformLaunch();
        }
        // Otherwise check for pending attach
        else if (_pendingAttachProcessId.HasValue) {
            var attachProcessId = _pendingAttachProcessId.Value;
            _pendingAttachProcessId = null;
            PerformAttach(attachProcessId);
            // The target may have been started with DOTNET_DefaultDiagnosticPortSuspend=1 - resume it so its runtime can start
            try {
                await DiagnosticClientHelper.DiagnosticClientResumeRuntime(attachProcessId);
            }
            catch (Exception ex) {
                _logger?.Invoke($"Failed to resume the runtime of the attach target (already running?): {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Continue execution
    /// </summary>
    public void HandleContinueRequest() {
        _logger?.Invoke("Continue");
        ContinueWithVariableClearAllowSuperfluousContinue();
    }

    private void ContinueWithVariableClearAllowSuperfluousContinue() {
        ArgumentNullException.ThrowIfNull(_process);
        _variableManager.ClearAndDisposeHandleValues();
        _frameReferenceManager.Clear();

        var result = _process.TryContinue(false);
        if (result is Cor.CORDBG_E_SUPERFLOUS_CONTINUE) return;
        Marshal.ThrowExceptionForHR(result);
    }

    private void ContinueWithVariableClear() {
        ArgumentNullException.ThrowIfNull(_process);
        _variableManager.ClearAndDisposeHandleValues();
        _frameReferenceManager.Clear();

        _process.Continue(false);
    }

    /// <summary>
    /// Pause execution. Refused when the process is not in a state it can be stopped from, rather than
    /// skipped: the caller is told the program stopped, so it has to have stopped.
    ///
    /// For a short while after an attach has landed, ICorDebug reports the process as not running while
    /// the debuggee is plainly still executing - it is working through the synthetic attach events
    /// rather than sitting at a stop. Skipping the stop there left a client believing a running program
    /// had halted, with no way to find out: over DAP a running process and a stopped one answer a stack
    /// trace alike. The state clears in a moment, so a client that wants the pause can ask again.
    /// </summary>
    public void Pause() {
        _logger?.Invoke("Pause");
        ArgumentNullException.ThrowIfNull(_process);
        if (!IsRunning)
            throw new InvalidOperationException("The program is not running, so it cannot be paused: it has either stopped already or has not finished starting");

        _process.Stop(0);
        _asyncStepper?.Disable();
    }

    /// <summary>
    /// Whether ICorDebug reports the debuggee as executing, which is the one state <see cref="Pause"/>
    /// can stop it from.
    /// </summary>
    public bool IsRunning => _process?.IsRunning() ?? false;

    /// <summary>
    /// Step to the next line
    /// </summary>
    public async void StepNext(int threadId) {
        _logger?.Invoke($"StepNext on thread {threadId}");
        if (_threads.TryGetValue(threadId, out var thread)) {
            var frame = thread.GetActiveFrame();
            if (frame is not ICorDebugILFrame ilFrame) throw new InvalidOperationException("Active frame is not an IL frame");
            if (_stepper is not null) throw new InvalidOperationException("A step operation is already in progress");

            // Try async stepping first
            if (_asyncStepper is not null) {
                var (handledByAsyncStepper, useSimpleStepper) = await _asyncStepper.TrySetupAsyncStep(thread, AsyncStepper.StepType.StepOver);
                if (handledByAsyncStepper) {
                    if (useSimpleStepper is false) {
                        ContinueWithVariableClear();
                        return;
                    }
                }
            }

            var stepper = SetupStepper(thread, AsyncStepper.StepType.StepOver);
            ContinueWithVariableClear();
        }
    }

    /// <summary>
    /// Step into
    /// </summary>
    public async void StepIn(int threadId) {
        _logger?.Invoke($"StepIn on thread {threadId}");
        if (_threads.TryGetValue(threadId, out var thread)) {
            var frame = thread.GetActiveFrame();
            if (frame is not null) {
                // Try async stepping first
                if (_asyncStepper is not null) {
                    var (handledByAsyncStepper, useSimpleStepper) = await _asyncStepper.TrySetupAsyncStep(thread, AsyncStepper.StepType.StepIn);
                    if (handledByAsyncStepper) {
                        if (useSimpleStepper is false) {
                            ContinueWithVariableClear();
                            return;
                        }
                    }
                }

                var stepper = SetupStepper(thread, AsyncStepper.StepType.StepIn);
                ContinueWithVariableClear();
            }
        }
    }

    /// <summary>
    /// Step out
    /// </summary>
    public async void StepOut(int threadId) {
        _logger?.Invoke($"StepOut on thread {threadId}");
        if (_threads.TryGetValue(threadId, out var thread)) {
            var frame = thread.GetActiveFrame();
            if (frame is not null) {
                // Try async stepping first
                if (_asyncStepper is not null) {
                    var (handledByAsyncStepper, useSimpleStepper) = await _asyncStepper.TrySetupAsyncStep(thread, AsyncStepper.StepType.StepOut);
                    if (handledByAsyncStepper) {
                        if (useSimpleStepper is false) {
                            ContinueWithVariableClear();
                            return;
                        }
                    }
                }

                var stepper = SetupStepper(thread, AsyncStepper.StepType.StepOut);
                if (stepper is not null) {
                    ContinueWithVariableClear();
                }
            }
        }
    }

    /// <summary>
    /// Set breakpoints for a source file with optional conditions
    /// </summary>
    public List<BreakpointManager.BreakpointInfo> SetBreakpoints(string filePath, BreakpointRequest[] breakpoints) {
        //System.Diagnostics.Debugger.Launch();
        _logger?.Invoke($"SetBreakpoints: {filePath}, breakpoints: {string.Join(",", breakpoints.Select(b => $"L{b.Line} {(b.Column is not null ? $"C{b.Column}" : null)} {(b.Condition is not null ? $"[{b.Condition}]" : null)}"))}");

        // Deactivate and clear existing breakpoints for this file
        var existingBreakpoints = _breakpointManager.GetBreakpointsForFile(filePath);
        foreach (var bp in existingBreakpoints) {
            if (bp.CorBreakpoint is not null) {
                try {
                    bp.CorBreakpoint.Activate(false);
                }
                catch (Exception ex) {
                    _logger?.Invoke($"Error deactivating breakpoint: {ex.Message}");
                }
            }
        }
        _breakpointManager.ClearBreakpointsForFile(filePath);

        // Create new breakpoints
        var result = new List<BreakpointManager.BreakpointInfo>();
        foreach (var request in breakpoints) {
            var bp = _breakpointManager.CreateBreakpoint(filePath, request.Line, request.Column, request.Condition, request.HitCondition);

            // Try to bind the breakpoint if we have a process
            if (_process is not null) {
                TryBindBreakpoint(bp);
            }
            else {
                // No process yet, mark as pending
                bp.Message = "Breakpoint has not been processed by the debugger.";
            }

            result.Add(bp);
        }

        return result;
    }

    public List<BreakpointManager.BreakpointInfo> SetFunctionBreakpoints(FunctionBreakpointRequest[] breakpoints) {
        foreach (var bp in _breakpointManager.GetFunctionBreakpoints()) {
            foreach (var binding in bp.FunctionBindings) {
                try { binding.CorBreakpoint.Activate(false); }
                catch (Exception ex) { _logger?.Invoke($"Error deactivating function breakpoint: {ex.Message}"); }
            }
        }
        _breakpointManager.ClearFunctionBreakpoints();

        var result = new List<BreakpointManager.BreakpointInfo>();
        foreach (var request in breakpoints) {
            var bp = _breakpointManager.CreateFunctionBreakpoint(request.Name, request.Condition, request.HitCondition);
            try {
                _ = FunctionBreakpointPattern.Parse(request.Name);
                foreach (var module in _modules.Values) {
                    TryBindFunctionBreakpoint(bp, module);
                }
                if (!bp.Verified) bp.Message = _process is null
                    ? "Breakpoint has not been processed by the debugger."
                    : $"No functions matching '{request.Name}' were found.";
            }
            catch (ArgumentException ex) {
                bp.Message = ex.Message;
            }
            result.Add(bp);
        }
        return result;
    }

    /// <summary>
    /// Get all threads
    /// </summary>
    public List<(int id, string name)> GetThreads() {
        var result = new List<(int, string)>();
        if (_process is null) return result;

        try {
            var threads = _process.EnumerateThreads();
            foreach (var thread in threads) {
                result.Add((thread.GetId(), GetThreadDisplayName(thread)));
            }
        }
        catch (Exception ex) {
            _logger?.Invoke($"Error getting threads: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Get stack trace for a thread
    /// </summary>
    public List<StackFrameInfo> GetStackTrace(int threadId, int startFrame = 0, int? levels = null) {
        var result = new List<StackFrameInfo>();

        if (!_threads.TryGetValue(threadId, out var thread)) {
            return result;
        }

        try {
            var frames = EnumerateFramesForThread(thread);
            var filterFrames = frames.Skip(startFrame).Take(levels ?? int.MaxValue);

            foreach (var (index, frame) in filterFrames.Index()) {
                var stackFrameInfo = new StackFrameInfo {
                    Id = _frameReferenceManager.GetOrCreateFrameId(new ThreadId(threadId), new FrameStackDepth(startFrame + index)),
                    Name = null!,
                    Line = 0,
                    EndLine = 0,
                    Column = 0,
                    EndColumn = 0,
                    Source = null
                };
                if (frame is ICorDebugILFrame ilFrame) {
                    var function = ilFrame.GetFunction();
                    stackFrameInfo.Name = GetFunctionFormattedName(function);
                    var module = _modules[function.GetModule().GetBaseAddress()];
                    if (module.MetadataReader.HasSymbols) {
                        var ilOffset = ilFrame.GetIP().pnOffset;
                        var methodToken = function.GetToken();
                        var sourceInfo = module.MetadataReader.GetSourceLocationForOffset(methodToken, ilOffset);
                        if (sourceInfo is not null) {
                            stackFrameInfo.Line = sourceInfo.Value.startLine;
                            stackFrameInfo.EndLine = sourceInfo.Value.endLine;
                            stackFrameInfo.Column = sourceInfo.Value.startColumn;
                            stackFrameInfo.EndColumn = sourceInfo.Value.endColumn;
                            stackFrameInfo.Source = sourceInfo.Value.sourceFilePath;
                        }
                    }
                }
                else if (frame is ICorDebugInternalFrame internalFrame) {
                    stackFrameInfo.Name = internalFrame.GetFrameType().ToDisplayName();
                }
                else if (frame is ICorDebugNativeFrame nativeFrame) {
                    stackFrameInfo.Name = "[Native Frame]";
                }
                else throw new ArgumentOutOfRangeException(nameof(frame), "Unknown frame type");
                result.Add(stackFrameInfo);
            }
        }
        catch (Exception ex) {
            _logger?.Invoke($"Error getting stack trace: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Get scopes for a stack frame
    /// </summary>
    public List<ScopeInfo> GetScopes(int frameId) {
        var result = new List<ScopeInfo>();

        var frameInfo = _frameReferenceManager.GetFrameInfoById(frameId);
        if (frameInfo is not var (threadId, frameStackDepth)) return result;
        var frame = GetFrameForThreadIdAndStackDepth(threadId, frameStackDepth);
        if (frame is not ICorDebugILFrame ilFrame) return result;

        var localVariables = ilFrame.GetLocalVariables();
        var arguments = ilFrame.GetArguments();
        var thread = _threads.GetValueOrDefault(threadId.Value);
        ArgumentNullException.ThrowIfNull(thread);
        var hasCurrentException = thread.TryGetCurrentException(out _) is Cor.S_OK;
        if (localVariables.Length is 0 && arguments.Length is 0 && !hasCurrentException) return result;

        // can this just be the same reference?
        var localsRef = _variableManager.CreateReference(new VariablesReference(StoredReferenceKind.Scope, null, threadId, frameStackDepth, null));
        result.Add(new ScopeInfo {
            Name = "Locals",
            VariablesReference = localsRef,
            Expensive = false
        });
        return result;
    }

    /// <summary>
    /// Get variables for a scope
    /// </summary>
    public async Task<List<VariableInfo>> GetVariables(int variablesReferenceInt) {
        var result = new List<VariableInfo>();

        var variablesReferenceNullable = _variableManager.GetReference(variablesReferenceInt);
        if (variablesReferenceNullable is not { } variablesReference) throw new ArgumentException("Invalid variables reference");
        try {
            if (variablesReference.ReferenceKind is StoredReferenceKind.Scope) {
                var ilFrame = GetIlFrameForThreadIdAndStackDepth(variablesReference.ThreadId, variablesReference.FrameStackDepth);
                var corDebugFunction = ilFrame.GetFunction();
                var module = _modules[corDebugFunction.GetModule().GetBaseAddress()];
                await AddCurrentException(result, variablesReference.ThreadId, variablesReference.FrameStackDepth);
                var classContainingHoistedLocalsValue = await AddArguments(module, corDebugFunction, result, variablesReference.ThreadId, variablesReference.FrameStackDepth);
                await AddLocalVariables(module, corDebugFunction, result, variablesReference.ThreadId, variablesReference.FrameStackDepth, classContainingHoistedLocalsValue);
            }
            else if (variablesReference.ReferenceKind is StoredReferenceKind.StackVariable) {
                if (variablesReference.DebuggerProxyInstance is not null) {
                    // get the public members of the debugger proxy instance instead
                    var objectValue = variablesReference.DebuggerProxyInstance.UnwrapDebugValueToObject();
                    await AddMembersAndStaticPseudoVariable(variablesReference.DebuggerProxyInstance, objectValue.GetExactType(), variablesReference.ThreadId, variablesReference.FrameStackDepth, result, includeNonPublicGroup: false);
                    var rawValueVariablesReference = _variableManager.CreateReference(new VariablesReference(StoredReferenceKind.StackVariable, variablesReference.ObjectValue, variablesReference.ThreadId, variablesReference.FrameStackDepth, null));
                    var rawValuePseudoVariable = new VariableInfo {
                        Name = "Raw View",
                        Value = "",
                        Type = "",
                        PresentationHint = new VariablePresentationHint { Kind = PresentationHintKind.Class },
                        VariablesReference = rawValueVariablesReference
                    };
                    result.Add(rawValuePseudoVariable);
                    SortMembers(result);
                    return result;
                }
                var unwrappedDebugValue = variablesReference.ObjectValue!.UnwrapDebugValue();

                if (unwrappedDebugValue is ICorDebugArrayValue arrayValue) {
                    await AddArrayElements(arrayValue, variablesReference.ThreadId, variablesReference.FrameStackDepth, result);
                }
                else if (unwrappedDebugValue is ICorDebugObjectValue objectValue) {
                    await AddMembersAndStaticPseudoVariable(variablesReference.ObjectValue!, objectValue.GetExactType(), variablesReference.ThreadId, variablesReference.FrameStackDepth, result);
                    SortMembers(result);
                }
                else {
                    throw new ArgumentOutOfRangeException(nameof(unwrappedDebugValue));
                }
            }
            else if (variablesReference.ReferenceKind is StoredReferenceKind.NonPublicStackVariable) {
                var objectValue = variablesReference.ObjectValue!.UnwrapDebugValueToObject();
                _ = await AddMembers(variablesReference.ObjectValue!, objectValue.GetExactType(), variablesReference.ThreadId, variablesReference.FrameStackDepth, result, MemberVisibility.NonPublic);
                SortMembers(result);
            }
            else if (variablesReference.ReferenceKind is StoredReferenceKind.StaticClassVariable) {
                var objectValue = variablesReference.ObjectValue!.UnwrapDebugValueToObject();
                // User code types show all their members inline, only non user (library) types get the 'Non-Public members' group
                var visibility = IsUserCodeType(objectValue.GetExactType()) ? MemberVisibility.All : MemberVisibility.Public;
                var hasNonPublicMembers = await AddStaticMembers(variablesReference.ObjectValue!, objectValue.GetExactType(), variablesReference.ThreadId, variablesReference.FrameStackDepth, result, visibility);
                if (hasNonPublicMembers) {
                    result.Add(new VariableInfo {
                        Name = "Non-Public members",
                        Value = "",
                        Type = "",
                        PresentationHint = new VariablePresentationHint { Kind = PresentationHintKind.Class },
                        VariablesReference = _variableManager.CreateReference(new VariablesReference(StoredReferenceKind.NonPublicStaticClassVariable, variablesReference.ObjectValue, variablesReference.ThreadId, variablesReference.FrameStackDepth, null))
                    });
                }
                SortMembers(result);
            }
            else if (variablesReference.ReferenceKind is StoredReferenceKind.NonPublicStaticClassVariable) {
                var objectValue = variablesReference.ObjectValue!.UnwrapDebugValueToObject();
                _ = await AddStaticMembers(variablesReference.ObjectValue!, objectValue.GetExactType(), variablesReference.ThreadId, variablesReference.FrameStackDepth, result, MemberVisibility.NonPublic);
                SortMembers(result);
            }
        }
        catch (Exception ex) {
            _logger?.Invoke($"Error getting variables: {ex.Message}, {ex}");
            throw;
        }

        return result;
    }

    /// <summary>
    /// Evaluate an expression
    /// </summary>
    public async Task<VariableInfo> Evaluate(string expression, int? frameId) {
        _logger?.Invoke($"Evaluate: {expression}");
        if (frameId is null or 0) throw new InvalidOperationException("Frame ID is required for evaluation");

        var frameInfo = _frameReferenceManager.GetFrameInfoById(frameId.Value);
        if (frameInfo is not var (threadId, frameStackDepth)) throw new InvalidOperationException("Frame ID does not exist");
        var thread = _threads.GetValueOrDefault(threadId.Value);
        ArgumentNullException.ThrowIfNull(thread);

        var evalContext = new CompiledExpressionEvaluationContext(thread, threadId, frameStackDepth);
        ArgumentNullException.ThrowIfNull(_expressionEvaluator);
        using var result = await _expressionEvaluator.Evaluate(expression, evalContext);

        if (result.Error is not null) {
            _logger?.Invoke($"Evaluation error: {result.Error}");
            return new VariableInfo {
                Name = null!,
                Value = result.Error,
                Type = null,
                PresentationHint = new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation },
                VariablesReference = 0
            };
        }
        var (friendlyTypeName, value, debuggerProxyInstance, resultIsError) = await GetValueForCorDebugValueAsync(result.Value!, threadId, frameStackDepth, true);
        VariablePresentationHint? variablePresentationHint = resultIsError ? new VariablePresentationHint { Attributes = AttributesValue.FailedEvaluation } : null;
        var variablesReference = GetVariablesReference(result.Value!, friendlyTypeName, threadId, frameStackDepth, debuggerProxyInstance);
        var variableInfo = new VariableInfo {
            Name = null!,
            Value = value,
            Type = friendlyTypeName,
            PresentationHint = variablePresentationHint,
            VariablesReference = variablesReference
        };
        if (variablesReference != 0) result.RelinquishResultHandleOwnership();
        return variableInfo;
    }

    /// <summary>
    /// Terminate the debugged process
    /// </summary>
    public void Terminate() {
        _logger?.Invoke("Terminate");
        if (_process is not null) {
            try {
                _process.Terminate(0);
            }
            catch (Exception ex) {
                _logger?.Invoke($"Error terminating process: {ex.Message}");
            }
        }
        Dispose();
    }

    /// <summary>
    /// Disconnect from the debuggee and Dispose
    /// </summary>
    public void Disconnect(bool terminateDebuggee) {
        _logger?.Invoke($"Disconnect (terminate: {terminateDebuggee})");

        if (terminateDebuggee) {
            Terminate();
        }
        else {
            if (_process is not null && _isAttached && _process?.TryIsRunning(out var isRunning) is Cor.S_OK && isRunning) {
                var hResult = _process.TryStop(0);
                if (hResult is not (Cor.S_OK or Cor.CORDBG_E_PROCESS_TERMINATED)) _logger?.Invoke($"Error stopping process during disconnect: {hResult}");
            }
            Dispose();
        }
    }

    public async Task<ExceptionInfo> ExceptionInfo(ThreadId threadId) {
        _logger?.Invoke($"ExceptionInfo for thread {threadId.Value}");
        var thread = _process!.GetThread(threadId.Value);
        if (thread.TryGetCurrentException(out var currentException) is not Cor.S_OK) {
            _logger?.Invoke("No current exception");
            throw new InvalidOperationException("No current exception on thread");
        }

        var frameStackDepth = new FrameStackDepth(0);
        var (friendlyTypeName, _, _, _) = await GetValueForCorDebugValueAsync(currentException, threadId, frameStackDepth, false);

        var hResult = await GetExceptionPropertyValue("HResult");
        var source = await GetExceptionPropertyValue("Source");
        var message = await GetExceptionPropertyValue("Message");
        var stackTrace = await GetExceptionPropertyValue("StackTrace");

        var typeNameSpan = friendlyTypeName.AsSpan();
        var lastDot = typeNameSpan.LastIndexOf('.');
        var exceptionTypeNameNoNamespace = lastDot is not -1
            ? typeNameSpan[(lastDot + 1)..].ToString()
            : friendlyTypeName;

        var exceptionInfo = new ExceptionInfo {
            ExceptionId = $"CLR/{friendlyTypeName}",
            Description = $"Exception thrown: '{friendlyTypeName}' in {source}.dll: '{message}'",
            BreakMode = ExceptionBreakMode.Always,
            Code = 0,
            Details = new ExceptionInfo.ExceptionDetails {
                Message = message,
                TypeName = exceptionTypeNameNoNamespace,
                FullTypeName = friendlyTypeName,
                EvaluateName = "$exception",
                StackTrace = stackTrace,
                InnerException = [],
                FormattedDescription = $"**{friendlyTypeName}:** '{message}'",
                HResult = int.Parse(hResult),
                Source = source
            }
        };
        return exceptionInfo;

        async Task<string> GetExceptionPropertyValue(string propertyName) {
            var propertyValue = await currentException.GetPropertyValue(ProcessRuntimeEventsUntilEvalEvent, EvalStatus, (ICorDebugILFrame)thread.GetActiveFrame(), propertyName)
                ?? throw new InvalidOperationException($"Exception property '{propertyName}' returned no value");
            try {
                var (_, value, _, _) = await GetValueForCorDebugValueAsync(propertyValue, threadId, frameStackDepth, false);
                return value;
            }
            finally {
                if (propertyValue is ICorDebugHandleValue handle) handle.TryDispose();
            }
        }
    }
}