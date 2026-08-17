namespace DotNet.Debugging.CorApi;

public sealed class DataBreakpointCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugProcess Process { get; }
    public ICorDebugThread Thread { get; }
    public byte Context { get; }
    public uint ContextSize { get; }

    public DataBreakpointCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess, ICorDebugThread pThread, byte pContext, uint contextSize) {
        Process = pProcess;
        Thread = pThread;
        Context = pContext;
        ContextSize = contextSize;
    }
}