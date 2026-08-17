namespace DotNet.Debugging.CorApi;

public sealed class AfterGarbageCollectionCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugProcess Process { get; }

    public AfterGarbageCollectionCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess) {
        Process = pProcess;
    }
}