using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Logging;

namespace DotNet.Debugging.Engine;

public partial class ManagedDebugger {
    private void HandleProcessCreated(CreateProcessCorDebugManagedCallbackEventArgs callbackEvent) {
        // A remote (mobile) attach has no local process to attach to, the ICorDebugProcess only exists
        // once the on-device runtime connects back and raises this callback
        if (process == null && isRemoteAttach) {
            process = callbackEvent.Process;
            ProcessId = process.GetId();
            DebuggerLoggingService.LogMessage($"The remote debuggee connected, PID: {ProcessId}");
        }
        // Debug.WriteLine, Trace.WriteLine and Debugger.Log reach the debugger (the LogMessage callback) only while it
        // asks for them; without this the runtime writes them to the system log instead
        var result = callbackEvent.Process.TryEnableLogMessages(true);
        if (result != Cor.S_OK)
            DebuggerLoggingService.LogMessage($"The debuggee's log messages could not be enabled: 0x{result:X8}");
        ContinueProcess();
    }
    private void HandleProcessExited(ExitProcessCorDebugManagedCallbackEventArgs callbackEvent) {
        DebuggerLoggingService.LogMessage("Process exited");
        var exitCode = 0;
        // The runtime is shutting down, the OS process follows right after
        if (launchedProcess != null && launchedProcess.WaitForExit(2000))
            exitCode = launchedProcess.ExitCode;
        // ExitProcess is the runtime's final callback. Completing the queue fails a request stuck in
        // a func eval the debuggee died under, so it releases the lock instead of waiting forever
        eventQueue.Writer.TryComplete();
        OnExited?.Invoke(exitCode);
    }
    // Debug.WriteLine and friends: the runtime raises the callback for every message the debuggee logs
    private void HandleLogMessage(LogMessageCorDebugManagedCallbackEventArgs callbackEvent) {
        OnDebugMessage?.Invoke(callbackEvent.Message);
        ContinueProcess();
    }
}
