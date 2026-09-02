namespace DotNet.Debugging.CorApi;

public sealed class DataBreakpointCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugProcess Process { get; }
    public ICorDebugThread Thread { get; }
    // A copy of the thread context the runtime passed: the original only lives for the duration of the callback
    public byte[] Context { get; }

    public DataBreakpointCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess, ICorDebugThread pThread, byte[] context) {
        Process = pProcess;
        Thread = pThread;
        Context = context;
    }
}
