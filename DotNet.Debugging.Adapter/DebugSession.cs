using DotNet.Debugging.Common.Logging;
using DotNet.Debugging.Engine;
using DotNet.Debugging.Engine.Models;
using DotNet.Debugging.Adapter.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession : Session {
    private const int VariablesPageSize = 25;
    // Far above any reference number issued by the debugger's variable and frame managers
    private const int PagingHandlesStart = 1_000_000_000;

    private readonly Handles<SourceLocation> gotoHandles = new Handles<SourceLocation>();
    private readonly Handles<PagedVariablesReference> pagingHandles = new Handles<PagedVariablesReference>(PagingHandlesStart);
    private readonly Dictionary<int, string> logpointMessages = new Dictionary<int, string>();
    private readonly Dictionary<string, List<int>> logpointIdsByFile = new Dictionary<string, List<int>>();
    private readonly ExceptionFilterOptions allExceptionsFilter = new ExceptionFilterOptions();
    private readonly ExceptionFilterOptions userUnhandledExceptionsFilter = new ExceptionFilterOptions();
    private readonly ManagedDebugger session;

    private IDebugAgent debugAgent = null!;

    public DebugSession(Stream input, Stream output) : base(input, output) {
        session = new ManagedDebugger(message => CurrentSessionLogger.Debug($"[CorDebug] {message}"));

        session.OnStopped += TargetStopped;
        session.OnStopped2 += TargetStoppedAtSource;
        session.OnExceptionThrown += TargetExceptionThrown;
        session.OnExited += TargetExited;
        session.OnProcessStarted += SendProcessEvent;
        session.OnThreadStarted += TargetThreadStarted;
        session.OnThreadExited += TargetThreadStopped;
        session.OnModuleLoaded += AssemblyLoaded;
        session.OnModuleLoadedVerbose += AssemblyLoadedVerbose;
        session.OnOutput += TargetOutput;
        session.OnBreakpointChanged += BreakpointStatusChanged;
        session.SendRunInTerminalRequest += RunInTerminal;
    }

    protected override void OnUnhandledException(Exception ex) => debugAgent?.Dispose();

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
    /// <summary>
    /// Tells the client which process it is debugging. Without it a launched program's pid never reaches
    /// the client, which leaves it nothing to show, to log, or to kill if the session goes wrong.
    ///
    /// Sent for every shape of launch: the internal console and the two terminal ones raise it through
    /// the debugger, and a skipDebug run has no debugger so SkipDebugAgent calls this itself.
    ///
    /// An attach sends none, because the client named the process itself. A remote attach sends none
    /// either, and that is a limit of this engine rather than of the protocol - DAP describes a remote
    /// process with IsLocalProcess=false - because the remote path never learns a device-side pid it
    /// could report.
    /// </summary>
    internal void SendProcessEvent(int processId) {
        // The name is what the client asked to run, not what was executed: a managed dll is started
        // through the muxer, so the process's own executable is 'dotnet' and would say nothing about
        // which program is being debugged. Only a launch reaches here, so the configuration is a launch
        // one and its program has been verified present.
        var configuration = (LaunchConfiguration)debugAgent.Configuration;
        ArgumentNullException.ThrowIfNull(configuration.Program);

        Protocol.SendEvent(new ProcessEvent(configuration.Program) {
            SystemProcessId = processId,
            StartMethod = ProcessEvent.StartMethodValue.Launch,
            IsLocalProcess = true,
        });
    }
    private void TargetExited(int exitCode) {
        Protocol.SendEvent(new ExitedEvent(exitCode));
        Protocol.SendEvent(new TerminatedEvent());
    }
    private void TargetThreadStarted(int threadId) {
        Protocol.SendEvent(new ThreadEvent(ThreadEvent.ReasonValue.Started, threadId));
    }
    private void TargetThreadStopped(int threadId) {
        Protocol.SendEvent(new ThreadEvent(ThreadEvent.ReasonValue.Exited, threadId));
    }
    private void AssemblyLoaded(string id, string name, string path, bool isUserCode) {
        Protocol.SendEvent(new ModuleEvent(ModuleEvent.ReasonValue.New, new Module {
            Id = id, Name = name, Path = path, IsUserCode = isUserCode
        }));
    }

    private void AssemblyLoadedVerbose(ModuleLoadedInfo moduleInfo) {
        OnDebugDataReceived(moduleInfo.ToLoadedAssemblyMessage(debugAgent.Configuration.GetApplicationName(), debugAgent.Configuration.JustMyCode));
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