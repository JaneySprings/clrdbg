namespace DotNet.Debugging.CorApi;

public sealed class CreateConnectionCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugProcess Process { get; }
    public uint DwConnectionId { get; }
    public string ConnName { get; }

    public CreateConnectionCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess, uint dwConnectionId, string pConnName) {
        Process = pProcess;
        DwConnectionId = dwConnectionId;
        ConnName = pConnName;
    }
}