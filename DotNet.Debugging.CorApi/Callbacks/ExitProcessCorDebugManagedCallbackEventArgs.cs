namespace DotNet.Debugging.CorApi;

public sealed class ExitProcessCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugProcess Process { get; }

    public ExitProcessCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess) {
        Process = pProcess;
    }
}