namespace DotNet.Debugging.CorApi;

public sealed class DebuggerErrorCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugProcess Process { get; }
    public int ErrorHR { get; }
    public uint ErrorCode { get; }

    public DebuggerErrorCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess, int errorHR, uint errorCode) {
        Process = pProcess;
        ErrorHR = errorHR;
        ErrorCode = errorCode;
    }
}