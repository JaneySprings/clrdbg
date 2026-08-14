using DotNet.Debugging.Common.Logging;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Models;
using DotNet.Debugging.CorApi.PresentationHintModels;
using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using DebugProtocol = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public class DebugSession : Session {
    private const int VariablesPageSize = 25;
    // Far above any reference number issued by the debugger's variable and frame managers
    private const int PagingHandlesStart = 1_000_000_000;

    private BaseLaunchAgent launchAgent = null!;

    private readonly Handles<SourceLocation> gotoHandles = new Handles<SourceLocation>();
    private readonly Handles<PagedVariablesReference> pagingHandles = new Handles<PagedVariablesReference>(PagingHandlesStart);
    private readonly Dictionary<int, string> logpointMessages = new Dictionary<int, string>();
    private readonly Dictionary<string, List<int>> logpointIdsByFile = new Dictionary<string, List<int>>();
    private readonly ManagedDebugger session;

    private readonly ExceptionFilterOptions allExceptionsFilter = new ExceptionFilterOptions();
    private readonly ExceptionFilterOptions userUnhandledExceptionsFilter = new ExceptionFilterOptions();

    private bool justMyCode = true;
    private string? debuggeeProcessName;

    public DebugSession(Stream input, Stream output) : base(input, output) {
        session = new ManagedDebugger(message => CurrentSessionLogger.Debug($"[CorDebug] {message}"));

        session.OnStopped += TargetStopped;
        session.OnStopped2 += TargetStoppedAtSource;
        session.OnExceptionThrown += TargetExceptionThrown;
        session.OnContinued += TargetContinued;
        session.OnExited += TargetExited;
        session.OnTerminated += TargetTerminated;
        session.OnThreadStarted += TargetThreadStarted;
        session.OnThreadExited += TargetThreadStopped;
        session.OnModuleLoaded += AssemblyLoaded;
        session.OnModuleLoadedVerbose += AssemblyLoadedVerbose;
        session.OnOutput += TargetOutput;
        session.OnBreakpointChanged += BreakpointStatusChanged;
        session.SendRunInTerminalRequest += RunInTerminal;
    }

    protected override void OnUnhandledException(Exception ex) => launchAgent?.Dispose();

    #region Initialize
    protected override InitializeResponse HandleInitializeRequest(InitializeArguments arguments) {
        return new InitializeResponse() {
            SupportsConfigurationDoneRequest = true,
            SupportsTerminateRequest = true,
            SupportsEvaluateForHovers = true,
            SupportsExceptionInfoRequest = true,
            SupportsConditionalBreakpoints = true,
            SupportsHitConditionalBreakpoints = true,
            SupportsFunctionBreakpoints = true,
            SupportsLogPoints = true,
            SupportsExceptionFilterOptions = true,
            SupportsCompletionsRequest = true,
            SupportsSetVariable = true,
            SupportsGotoTargetsRequest = true,
            ExceptionBreakpointFilters = new List<ExceptionBreakpointsFilter> {
                ExceptionsFilter.AllExceptions,
                ExceptionsFilter.UserUnhandledExceptions
            }
        };
    }
    #endregion Initialize
    #region Launch
    protected override LaunchResponse HandleLaunchRequest(LaunchArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var configuration = new LaunchConfiguration(arguments.ConfigurationProperties);
            justMyCode = configuration.JustMyCode;
            launchAgent = configuration.GetLaunchAgent();
            launchAgent.Launch(this);
            InvokeDebugger(() => launchAgent.Connect(session));
            // Breakpoints arrive after this event and the launch itself is deferred until 'ConfigurationDone'
            Protocol.SendEvent(new InitializedEvent());
            return new LaunchResponse();
        });
    }
    #endregion Launch
    #region Attach
    protected override AttachResponse HandleAttachRequest(AttachArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var configuration = new LaunchConfiguration(arguments.ConfigurationProperties);
            if (configuration.ProcessId == 0)
                throw new ProtocolException("Missing process ID");

            justMyCode = configuration.JustMyCode;
            launchAgent = configuration.GetLaunchAgent();
            launchAgent.Launch(this);
            InvokeDebugger(() => launchAgent.Connect(session));
            // Breakpoints arrive after this event and the attach itself is deferred until 'ConfigurationDone'
            Protocol.SendEvent(new InitializedEvent());
            return new AttachResponse();
        });
    }
    #endregion Attach
    #region ConfigurationDone
    protected override ConfigurationDoneResponse HandleConfigurationDoneRequest(ConfigurationDoneArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            InvokeDebugger(() => session.ConfigurationDone());
            return new ConfigurationDoneResponse();
        });
    }
    #endregion ConfigurationDone
    #region Terminate
    protected override TerminateResponse HandleTerminateRequest(TerminateArguments arguments) {
        try {
            if (launchAgent is DebugLaunchAgent or AttachLaunchAgent)
                InvokeDebugger(() => session.Terminate());
        }
        catch (Exception ex) {
            CurrentSessionLogger.Error($"[Handled] Failed to terminate the debuggee {ex}");
        }
        finally {
            launchAgent?.Dispose();
            // The debugger detaches on 'Terminate' and can no longer deliver the exit event itself
            Protocol.SendEvent(new TerminatedEvent());
        }
        return new TerminateResponse();
    }
    #endregion Terminate
    #region Disconnect
    protected override DisconnectResponse HandleDisconnectRequest(DisconnectArguments arguments) {
        try {
            if (launchAgent is DebugLaunchAgent or AttachLaunchAgent) {
                // Per the protocol, a launched debuggee is terminated by default while an attached one is left running
                var terminateDebuggee = arguments.TerminateDebuggee ?? launchAgent is DebugLaunchAgent;
                InvokeDebugger(() => session.Disconnect(terminateDebuggee));
            }
        }
        catch (Exception ex) {
            CurrentSessionLogger.Error($"[Handled] Failed to disconnect from the debuggee {ex}");
        }
        finally {
            launchAgent?.Dispose();
        }
        return new DisconnectResponse();
    }
    #endregion Disconnect
    #region Continue
    protected override ContinueResponse HandleContinueRequest(ContinueArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            InvokeDebugger(() => session.HandleContinueRequest());
            return new ContinueResponse() { AllThreadsContinued = true };
        });
    }
    #endregion Continue
    #region Next
    protected override NextResponse HandleNextRequest(NextArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            InvokeDebugger(() => session.StepNext(arguments.ThreadId));
            return new NextResponse();
        });
    }
    #endregion Next
    #region StepIn
    protected override StepInResponse HandleStepInRequest(StepInArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            InvokeDebugger(() => session.StepIn(arguments.ThreadId));
            return new StepInResponse();
        });
    }
    #endregion StepIn
    #region StepOut
    protected override StepOutResponse HandleStepOutRequest(StepOutArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            InvokeDebugger(() => session.StepOut(arguments.ThreadId));
            return new StepOutResponse();
        });
    }
    #endregion StepOut
    #region Pause
    protected override PauseResponse HandlePauseRequest(PauseArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            InvokeDebugger(() => session.Pause());
            return new PauseResponse();
        });
    }
    #endregion Pause
    #region SetExceptionBreakpoints
    protected override SetExceptionBreakpointsResponse HandleSetExceptionBreakpointsRequest(SetExceptionBreakpointsArguments arguments) {
        allExceptionsFilter.Reset();
        userUnhandledExceptionsFilter.Reset();

        if (arguments.Filters != null) {
            if (arguments.Filters.Contains(ExceptionsFilter.AllExceptions.Filter))
                allExceptionsFilter.Enable();
            if (arguments.Filters.Contains(ExceptionsFilter.UserUnhandledExceptions.Filter))
                userUnhandledExceptionsFilter.Enable();
        }
        if (arguments.FilterOptions == null || arguments.FilterOptions.Count == 0)
            return new SetExceptionBreakpointsResponse();

        foreach (var option in arguments.FilterOptions) {
            var filter = GetExceptionFilterOptions(option.FilterId);
            filter?.Enable(option.Condition);
        }
        return new SetExceptionBreakpointsResponse();
    }
    #endregion SetExceptionBreakpoints
    #region SetBreakpoints
    protected override SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var sourcePath = arguments.Source?.Path;
            if (string.IsNullOrEmpty(sourcePath))
                throw new ProtocolException("No source available for the breakpoint");

            var breakpointsInfos = arguments.Breakpoints ?? new List<SourceBreakpoint>();
            var requests = breakpointsInfos
                .Select(it => new BreakpointRequest(it.Line, it.Condition, it.HitCondition, it.Column))
                .ToArray();

            var breakpoints = InvokeDebugger(() => {
                var result = session.SetBreakpoints(sourcePath, requests);
                // Rebind logpoint messages to the new breakpoint identifiers
                if (logpointIdsByFile.TryGetValue(sourcePath, out var staleIds)) {
                    foreach (var staleId in staleIds)
                        logpointMessages.Remove(staleId);
                }
                var fileLogpointIds = new List<int>();
                for (var i = 0; i < result.Count && i < breakpointsInfos.Count; i++) {
                    if (string.IsNullOrEmpty(breakpointsInfos[i].LogMessage))
                        continue;
                    logpointMessages[result[i].Id] = breakpointsInfos[i].LogMessage;
                    fileLogpointIds.Add(result[i].Id);
                }
                logpointIdsByFile[sourcePath] = fileLogpointIds;
                return result;
            });

            return new SetBreakpointsResponse(breakpoints.Select(it => it.ToBreakpoint()).ToList());
        });
    }
    #endregion SetBreakpoints
    #region SetFunctionBreakpoints
    protected override SetFunctionBreakpointsResponse HandleSetFunctionBreakpointsRequest(SetFunctionBreakpointsArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var breakpointsInfos = arguments.Breakpoints ?? new List<FunctionBreakpoint>();
            var requests = breakpointsInfos.Select(it => {
                var functionName = it.Name;
                var functionParts = it.Name.Split('!');
                if (functionParts.Length == 2)
                    functionName = functionParts[1];

                return new FunctionBreakpointRequest(functionName, it.Condition, it.HitCondition);
            }).ToArray();

            var breakpoints = InvokeDebugger(() => session.SetFunctionBreakpoints(requests));
            return new SetFunctionBreakpointsResponse(breakpoints.Select(it => it.ToBreakpoint()).ToList());
        });
    }
    #endregion SetFunctionBreakpoints
    #region StackTrace
    protected override StackTraceResponse HandleStackTraceRequest(StackTraceArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var frames = InvokeDebugger(() => session.GetStackTrace(arguments.ThreadId, arguments.StartFrame ?? 0, arguments.Levels));
            return new StackTraceResponse(frames.Select(it => it.ToStackFrame()).ToList());
        });
    }
    #endregion StackTrace
    #region Scopes
    protected override ScopesResponse HandleScopesRequest(ScopesArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var scopes = InvokeDebugger(() => session.GetScopes(arguments.FrameId));
            return new ScopesResponse(scopes.Select(it => it.ToScope()).ToList());
        });
    }
    #endregion Scopes
    #region Variables
    protected override VariablesResponse HandleVariablesRequest(VariablesArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var pageOffset = 0;
            var variablesReference = arguments.VariablesReference;
            if (pagingHandles.TryGet(variablesReference, out var page) && page != null) {
                variablesReference = page.VariablesReference;
                pageOffset = page.Offset;
            }

            var variables = InvokeDebugger(() => session.GetVariables(variablesReference));
            var response = new VariablesResponse(variables
                .Skip(pageOffset)
                .Take(VariablesPageSize)
                .Select(it => it.ToVariable())
                .ToList());

            var remainingCount = variables.Count - pageOffset - VariablesPageSize;
            if (remainingCount > 0) {
                response.Variables.Add(new DebugProtocol.Variable {
                    Name = "[More]",
                    Value = string.Empty,
                    VariablesReference = pagingHandles.Create(new PagedVariablesReference(variablesReference, pageOffset + VariablesPageSize))
                });
            }

            return response;
        });
    }
    #endregion Variables
    #region Threads
    protected override ThreadsResponse HandleThreadsRequest(ThreadsArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var threads = InvokeDebugger(() => session.GetThreads());
            return new ThreadsResponse(threads.Select(it => new DebugProtocol.Thread(it.id, it.name.ToThreadName(it.id))).ToList());
        });
    }
    #endregion Threads
    #region Evaluate
    protected override EvaluateResponse HandleEvaluateRequest(EvaluateArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var expression = arguments.TrimExpression();
            if (string.IsNullOrEmpty(expression))
                throw new ProtocolException("Expression is empty");

            var variable = InvokeDebugger(() => session.Evaluate(expression, arguments.FrameId));
            if (variable.PresentationHint?.Attributes == AttributesValue.FailedEvaluation)
                throw new ProtocolException(variable.Value);

            return new EvaluateResponse() {
                Result = variable.Value,
                Type = variable.Type,
                VariablesReference = variable.VariablesReference
            };
        });
    }
    #endregion Evaluate
    #region ExceptionInfo
    protected override ExceptionInfoResponse HandleExceptionInfoRequest(ExceptionInfoArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var exception = InvokeDebugger(() => session.ExceptionInfo(new ThreadId(arguments.ThreadId)));
            return exception.ToExceptionInfoResponse();
        });
    }
    #endregion ExceptionInfo
    // #region Completions
    // protected override CompletionsResponse HandleCompletionsRequest(CompletionsArguments arguments) {
    //     return ServerExtensions.DoSafe(() => {
    //         return new CompletionsResponse();
    //     });
    // }
    // #endregion Completions
    #region SetVariable
    protected override SetVariableResponse HandleSetVariableRequest(SetVariableArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var variablesReference = arguments.VariablesReference;
            if (pagingHandles.TryGet(variablesReference, out var page) && page != null)
                variablesReference = page.VariablesReference;

            var variable = InvokeDebugger(() => session.SetVariableValue(variablesReference, arguments.Name.ToVariableName(), arguments.Value));
            return variable.ToVariable().ToSetVariableResponse();
        });
    }
    #endregion SetVariable
    #region GotoTargets
    protected override GotoTargetsResponse HandleGotoTargetsRequest(GotoTargetsArguments arguments) {
        var targetId = gotoHandles.Create(new SourceLocation(arguments.Source.Path, arguments.Line));
        return arguments.ToJumpToCursorTarget(targetId);
    }
    protected override GotoResponse HandleGotoRequest(GotoArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            var target = gotoHandles.Get(arguments.TargetId, null);
            if (target == null)
                throw new ProtocolException("GotoTarget not found");

            InvokeDebugger(() => session.SetNextStatement(arguments.ThreadId, target.FileName, target.Line));
            Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Goto) {
                ThreadId = arguments.ThreadId,
                AllThreadsStopped = true,
            });
            return new GotoResponse();
        });
    }
    #endregion GotoTargets


    private void TargetStopped(int threadId, string reason) {
        ResetHandles();
        Protocol.SendEvent(new StoppedEvent(reason.ToStoppedReason()) {
            ThreadId = threadId,
            AllThreadsStopped = true,
        });
    }
    private void TargetStoppedAtSource(int threadId, string filePath, int line, int column, string reason, List<int>? hitBreakpointIds, DecompiledSourceInfo? decompiledSourceInfo) {
        ResetHandles();
        if (hitBreakpointIds != null && hitBreakpointIds.Count > 0) {
            var logpointIds = hitBreakpointIds.Where(logpointMessages.ContainsKey).ToList();
            foreach (var breakpointId in logpointIds)
                OnOutputDataReceived(session.ToInterpolatedLogMessage(logpointMessages[breakpointId], threadId));
            // Do not stop when only logpoints were hit at this location
            if (logpointIds.Count == hitBreakpointIds.Count) {
                session.HandleContinueRequest();
                return;
            }
        }

        Protocol.SendEvent(new StoppedEvent(reason.ToStoppedReason()) {
            ThreadId = threadId,
            AllThreadsStopped = true,
            HitBreakpointIds = hitBreakpointIds,
        });
    }
    private void TargetExceptionThrown(int threadId, ExceptionStopKind kind) {
        ResetHandles();
        var exceptionType = session.GetCurrentExceptionTypeName(threadId);
        var shouldStop = kind switch {
            ExceptionStopKind.Unhandled => true,
            ExceptionStopKind.UserUnhandled => userUnhandledExceptionsFilter.ShouldStopOnException(exceptionType),
            _ => allExceptionsFilter.ShouldStopOnException(exceptionType)
        };
        if (!shouldStop) {
            session.HandleContinueRequest();
            return;
        }

        Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Exception) {
            Description = kind == ExceptionStopKind.UserUnhandled ? "Paused on user-unhandled exception" : "Paused on exception",
            Text = exceptionType ?? "Exception",
            ThreadId = threadId,
            AllThreadsStopped = true,
        });
    }
    private void TargetContinued(int threadId) {
        Protocol.SendEvent(new ContinuedEvent() {
            ThreadId = threadId,
            AllThreadsContinued = true,
        });
    }
    private void TargetExited() {
        Protocol.SendEvent(new ExitedEvent(0));
    }
    private void TargetTerminated() {
        Protocol.SendEvent(new TerminatedEvent());
    }
    private void TargetThreadStarted(int threadId, string name) {
        Protocol.SendEvent(new ThreadEvent(ThreadEvent.ReasonValue.Started, threadId));
    }
    private void TargetThreadStopped(int threadId, string name) {
        Protocol.SendEvent(new ThreadEvent(ThreadEvent.ReasonValue.Exited, threadId));
    }
    private void AssemblyLoaded(string id, string name, string path) {
        Protocol.SendEvent(new ModuleEvent(ModuleEvent.ReasonValue.New, new Module {
            Id = id,
            Name = name,
            Path = path,
        }));
    }
    private void AssemblyLoadedVerbose(ModuleLoadedInfo moduleInfo) {
        debuggeeProcessName ??= moduleInfo.ProcessId.ToProcessName();
        OnDebugDataReceived(moduleInfo.ToLoadedAssemblyMessage(debuggeeProcessName, justMyCode));
    }
    private void BreakpointStatusChanged(BreakpointManager.BreakpointInfo breakpoint) {
        Protocol.SendEvent(new BreakpointEvent(BreakpointEvent.ReasonValue.Changed, breakpoint.ToBreakpoint()));
    }
    private void TargetOutput(string output, bool isError) {
        if (isError) OnErrorDataReceived(output);
        else OnOutputDataReceived(output);
    }
    private int RunInTerminal(LaunchInfo launchInfo) {
        var runInTerminalRequest = new RunInTerminalRequest() {
            Kind = launchInfo.LaunchRequestConsoleType == LaunchRequestConsoleType.ExternalTerminal
                ? RunInTerminalArguments.KindValue.External
                : RunInTerminalArguments.KindValue.Integrated,
            Arguments = new List<string>() { launchInfo.Program }.Concat(launchInfo.Arguments).ToList(),
            Cwd = launchInfo.Cwd,
            Env = launchInfo.Env.ToDictionary(it => it.Key, it => (object)it.Value),
            Title = $"{Path.GetFileName(launchInfo.Program)} [DEBUG]"
        };
        runInTerminalRequest.Env["DOTNET_DefaultDiagnosticPortSuspend"] = "1";

        var response = Protocol.SendClientRequestSync(runInTerminalRequest);
        if (response.ProcessId == null)
            throw new ProtocolException("RunInTerminalRequest did not return a process ID");

        return response.ProcessId.Value;
    }

    private void ResetHandles() {
        gotoHandles.Reset();
        pagingHandles.Reset();
    }
    private ExceptionFilterOptions? GetExceptionFilterOptions(string filterId) {
        if (filterId == ExceptionsFilter.AllExceptions.Filter)
            return allExceptionsFilter;
        if (filterId == ExceptionsFilter.UserUnhandledExceptions.Filter)
            return userUnhandledExceptionsFilter;

        return null;
    }

    private T InvokeDebugger<T>(Func<Task<T>> handler) {
        return Task.Run(async () => {
            using (await session.DapRequestAndRuntimeEventLock.LockAsync()) {
                await session.DrainRuntimeEventQueue().ConfigureAwait(false);
                return await handler.Invoke().ConfigureAwait(false);
            }
        }).GetAwaiter().GetResult();
    }
    private T InvokeDebugger<T>(Func<T> handler) {
        return InvokeDebugger(() => Task.FromResult(handler.Invoke()));
    }
    private void InvokeDebugger(Func<Task> handler) {
        InvokeDebugger<bool>(async () => {
            await handler.Invoke().ConfigureAwait(false);
            return true;
        });
    }
    private void InvokeDebugger(Action handler) {
        InvokeDebugger(() => {
            handler.Invoke();
            return true;
        });
    }
}