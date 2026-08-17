namespace DotNet.Debugging.CorApi;

public sealed class BreakpointSetErrorCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugThread Thread { get; }
    public ICorDebugBreakpoint Breakpoint { get; }
    public uint DwError { get; }

    public BreakpointSetErrorCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugBreakpoint pBreakpoint, uint dwError) {
        AppDomain = pAppDomain;
        Thread = pThread;
        Breakpoint = pBreakpoint;
        DwError = dwError;
    }
}