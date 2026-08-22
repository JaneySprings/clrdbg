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
        ContinueProcess();
    }
    private void HandleProcessExited(ExitProcessCorDebugManagedCallbackEventArgs callbackEvent) {
        DebuggerLoggingService.LogMessage("Process exited");
        var exitCode = 0;
        // The runtime is shutting down, the OS process follows right after
        if (launchedProcess != null && launchedProcess.WaitForExit(2000))
            exitCode = launchedProcess.ExitCode;
        OnExited?.Invoke(exitCode);
    }
    private void HandleLogMessage(LogMessageCorDebugManagedCallbackEventArgs callbackEvent) {
        DebuggerLoggingService.LogMessage($"Debuggee log: {callbackEvent.Message}");
        ContinueProcess();
    }
}
