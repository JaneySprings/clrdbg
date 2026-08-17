namespace DotNet.Debugging.CorApi;

public sealed class BreakpointCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugThread Thread { get; }
    public ICorDebugBreakpoint Breakpoint { get; }

    public BreakpointCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugBreakpoint pBreakpoint) {
        AppDomain = pAppDomain;
        Thread = pThread;
        Breakpoint = pBreakpoint;
    }
}