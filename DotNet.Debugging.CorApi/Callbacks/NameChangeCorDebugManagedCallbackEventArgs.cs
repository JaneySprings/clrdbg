namespace DotNet.Debugging.CorApi;

public sealed class NameChangeCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugThread Thread { get; }

    public NameChangeCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread) {
        AppDomain = pAppDomain;
        Thread = pThread;
    }
}