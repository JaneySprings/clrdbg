using DotNet.Debugging.Adapter.Extensions;
using DotNet.Debugging.Adapter.Logging;
using DotNet.Debugging.Adapter.Symbols;
using DotNet.Debugging.Engine;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Logging;
using DotNet.Debugging.Engine.Models;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Breakpoint = DotNet.Debugging.Engine.Models.Breakpoint;
using LaunchRequest = DotNet.Debugging.Engine.Models.LaunchRequest;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession : Session {
    private readonly Handles<SourceLocation> gotoHandles = new Handles<SourceLocation>();
    private readonly Handles<PagedVariablesReference> pagingHandles = new Handles<PagedVariablesReference>(1_000_000_000);
    private readonly Handles<string> moduleHandles = new Handles<string>(StringComparer.InvariantCulture);
    private readonly ExceptionFilterOptions allExceptionsFilter = new ExceptionFilterOptions();
    private readonly ExceptionFilterOptions userUnhandledExceptionsFilter = new ExceptionFilterOptions();
    private readonly ManagedDebugger session;

    private IDebugAgent debugAgent = null!;
    private SourceLinkResolver sourceLinkResolver = null!;
    private SourceFileMapper sourceFileMapper = null!;
    private SymbolsResolver symbolsResolver = null!;

    public DebugSession(Stream input, Stream output) : base(input, output) {
        DebuggerLoggingService.CustomLogger = new EngineLogger();
        session = new ManagedDebugger();

        session.OnStopped += TargetStopped;
        session.OnExceptionThrown += TargetExceptionThrown;
        session.OnExited += TargetExited;
        session.OnProcessStarted += TargetProcessStarted;
        session.OnThreadStarted += TargetThreadStarted;
        session.OnThreadExited += TargetThreadStopped;
        session.OnModuleLoaded += AssemblyLoaded;
        session.OnSymbolsRequested += SymbolsRequested;
        session.OnOutput += TargetOutput;
        session.OnLogPoint += TargetLogPoint;
        session.OnBreakpointChanged += BreakpointStatusChanged;
        session.OnTerminalLaunchRequested += TerminalLaunchRequested;
    }

    protected override void OnEmergencyStopReceived() => debugAgent?.Dispose();
    protected override bool OnTraceMessageReceived() => debugAgent?.Configuration?.Logging != null;

    private void TargetStopped(StopInfo stop) {
        ResetHandles();
        Protocol.SendEvent(new StoppedEvent(stop.Reason.ToStoppedReason()) {
            ThreadId = stop.ThreadId,
            AllThreadsStopped = true,
            HitBreakpointIds = stop.HitBreakpointIds,
        });
    }
    private void TargetExceptionThrown(ExceptionStopInfo exception) {
        ResetHandles();
        var shouldStop = exception.Kind switch {
            ExceptionStopKind.Unhandled => true,
            ExceptionStopKind.UserUnhandled => userUnhandledExceptionsFilter.ShouldStopOnException(exception.TypeName),
            _ => allExceptionsFilter.ShouldStopOnException(exception.TypeName)
        };
        if (!shouldStop) {
            session.Continue();
            return;
        }

        Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Exception) {
            Text = exception.ToDisplayMessage(),
            ThreadId = exception.ThreadId,
            AllThreadsStopped = true,
        });
    }
    private void TargetProcessStarted(int processId) {
        Protocol.SendEvent(new ProcessEvent(debugAgent.Configuration.GetApplicationName()) {
            SystemProcessId = processId,
            StartMethod = ProcessEvent.StartMethodValue.Launch,
            IsLocalProcess = true,
        });
    }
    private void TargetExited(int exitCode) {
        OnDebugDataReceived($"The program '{debugAgent.Configuration.GetApplicationName()}' has exited with code {exitCode} (0x{exitCode:x}).");
        Protocol.SendEvent(new ExitedEvent(exitCode));
        Protocol.SendEvent(new TerminatedEvent());
    }
    private void TargetThreadStarted(int threadId) {
        Protocol.SendEvent(new ThreadEvent(ThreadEvent.ReasonValue.Started, threadId));
    }
    private void TargetThreadStopped(int threadId) {
        Protocol.SendEvent(new ThreadEvent(ThreadEvent.ReasonValue.Exited, threadId));
    }
    private void SymbolsRequested(SymbolsRequest request) {
        if (symbolsResolver.HasSymbolServers)
            OnDebugDataReceived(string.Format(Resources.MsgPdbSearching, request.SymbolFileName));
        request.SymbolFilePath = symbolsResolver.FindSymbols(request.SymbolFileName, request.PdbGuid);
    }
    private void AssemblyLoaded(ModuleInfo module) {
        var justMyCode = debugAgent.Configuration.JustMyCode;
        OnDebugDataReceived(module.ToLoadedAssemblyMessage(debugAgent.Configuration.GetApplicationName(), session.ProcessId, justMyCode));
        Protocol.SendEvent(new ModuleEvent(ModuleEvent.ReasonValue.New, module.ToModule(moduleHandles.Create(module.Path), justMyCode)));
    }
    private void BreakpointStatusChanged(Breakpoint breakpoint) {
        if (breakpoint.Status == BreakpointStatus.SourceMismatch)
            OnDebugDataReceived($"Breakpoint warning: {breakpoint.ToStatusMessage()} - {breakpoint.FilePath}: {breakpoint.Line}");
        Protocol.SendEvent(new BreakpointEvent(BreakpointEvent.ReasonValue.Changed, breakpoint.ToBreakpoint(sourceLinkResolver, sourceFileMapper)));
    }
    private void TargetOutput(string output, bool isError) {
        // Print chunks directly, without trimming
        var category = isError ? OutputEvent.CategoryValue.Stderr : OutputEvent.CategoryValue.Stdout;
        Protocol.TrySendEvent(new OutputEvent(output) { Category = category });
    }
    private void TargetLogPoint(string message) {
        OnOutputDataReceived($"[LogPoint]: {message}");
    }
    private void TerminalLaunchRequested(LaunchRequest launchRequest) {
        if (debugAgent is not LaunchDebugAgent launchDebugAgent)
            throw new InvalidOperationException();

        ArgumentNullException.ThrowIfNull(launchDebugAgent.TerminalLauncher);
        launchRequest.ProcessId = launchDebugAgent.TerminalLauncher.LaunchProgram(launchRequest);
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

    // Requests run under the debugger's lock, off the protocol thread so the debugger can call back into the client meanwhile
    private T InvokeDebugger<T>(Func<Task<T>> handler) {
        return Task.Run(() => session.InvokeAsync(handler)).GetAwaiter().GetResult();
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
