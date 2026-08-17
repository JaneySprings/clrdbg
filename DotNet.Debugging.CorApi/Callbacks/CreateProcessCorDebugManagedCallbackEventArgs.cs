namespace DotNet.Debugging.CorApi;

public sealed class CreateProcessCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugProcess Process { get; }

    public CreateProcessCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess) {
        Process = pProcess;
    }
}