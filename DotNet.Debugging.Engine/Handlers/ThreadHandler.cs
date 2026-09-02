using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;

namespace DotNet.Debugging.Engine;

public partial class ManagedDebugger {
    private void HandleThreadCreated(CreateThreadCorDebugManagedCallbackEventArgs callbackEvent) {
        var threadId = callbackEvent.Thread.GetId();
        mainThreadId ??= threadId;
        threads[threadId] = callbackEvent.Thread;
        OnThreadStarted?.Invoke(threadId);
        ContinueProcess();
    }
    private void HandleThreadExited(ExitThreadCorDebugManagedCallbackEventArgs callbackEvent) {
        var threadId = callbackEvent.Thread.GetId();
        threads.Remove(threadId);
        exceptionThreads.Remove(threadId);
        exceptionStopKinds.Remove(threadId);
        exceptionModules.Remove(threadId);
        OnThreadExited?.Invoke(threadId);
        ContinueProcess();
    }
}
