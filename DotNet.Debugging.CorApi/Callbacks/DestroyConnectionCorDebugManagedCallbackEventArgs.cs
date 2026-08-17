namespace DotNet.Debugging.CorApi;

public sealed class DestroyConnectionCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugProcess Process { get; }
    public uint DwConnectionId { get; }

    public DestroyConnectionCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess, uint dwConnectionId) {
        Process = pProcess;
        DwConnectionId = dwConnectionId;
    }
}