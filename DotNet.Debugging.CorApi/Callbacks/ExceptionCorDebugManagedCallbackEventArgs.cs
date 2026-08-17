namespace DotNet.Debugging.CorApi;

public sealed class ExceptionCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugThread Thread { get; }
    public bool Unhandled { get; }

    public ExceptionCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, bool unhandled) {
        AppDomain = pAppDomain;
        Thread = pThread;
        Unhandled = unhandled;
    }
}