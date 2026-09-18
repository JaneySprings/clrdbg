using System.Diagnostics;
using System.Runtime.Versioning;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Extensions;
using DotNet.Debugging.Engine.Logging;

namespace DotNet.Debugging.Engine;

public partial class ManagedDebugger {
    private void HandleThreadCreated(CreateThreadCorDebugManagedCallbackEventArgs callbackEvent) {
        var threadId = callbackEvent.Thread.GetId();
        if (IsMainThreadCandidate(threadId))
            mainThreadId = threadId;
        threads[threadId] = callbackEvent.Thread;
        OnThreadStarted?.Invoke(threadId);
        ContinueProcess();
    }
    // A launched runtime announces its main thread first. An attach announces the threads that exist already in the
    // runtime's own order, which may begin with one of its workers: there the main thread is told by its id. On Linux
    // and Android it is the process id; on Apple platforms thread ids grow with creation, so it is the lowest one.
    // Windows ids say nothing and its attach order is arbitrary, there the system tells the oldest thread of the process
    private bool IsMainThreadCandidate(int threadId) {
        if (mainThreadId == null)
            return true;
        if (mainThreadId == ProcessId)
            return false;
        if (threadId == ProcessId)
            return true;
        if (!isRemoteAttach && OperatingSystem.IsWindows())
            return threadId == GetOldestThreadId();
        return threadId < mainThreadId;
    }
    // Asked of the system once, the oldest thread of a process stays the same one. Without an answer the first thread stands
    [SupportedOSPlatform("windows")]
    private int? GetOldestThreadId() {
        if (oldestThreadId != null)
            return oldestThreadId;
        try {
            using var systemProcess = Process.GetProcessById(ProcessId);
            oldestThreadId = systemProcess.GetOldestThreadId();
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogMessage($"Failed to find the oldest thread of the process: {ex.Message}");
        }
        return oldestThreadId;
    }
    private void HandleThreadExited(ExitThreadCorDebugManagedCallbackEventArgs callbackEvent) {
        var threadId = callbackEvent.Thread.GetId();
        threads.Remove(threadId);
        exceptionThreads.Remove(threadId);
        exceptionStopKinds.Remove(threadId);
        exceptionModules.Remove(threadId);
        exceptionAddresses.Remove(threadId);
        OnThreadExited?.Invoke(threadId);
        ContinueProcess();
    }
}
