using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;

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
    // Windows ids say nothing, the first thread stands
    private bool IsMainThreadCandidate(int threadId) {
        if (mainThreadId == null)
            return true;
        if (mainThreadId == ProcessId)
            return false;
        if (threadId == ProcessId)
            return true;
        var hasOrderedThreadIds = isRemoteAttach || !OperatingSystem.IsWindows();
        return hasOrderedThreadIds && threadId < mainThreadId;
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
