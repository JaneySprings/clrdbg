namespace DotNet.Debugging.CorApi;

public sealed class CustomNotificationCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugThread Thread { get; }
    public ICorDebugAppDomain AppDomain { get; }

    public CustomNotificationCorDebugManagedCallbackEventArgs(ICorDebugThread pThread, ICorDebugAppDomain pAppDomain) {
        Thread = pThread;
        AppDomain = pAppDomain;
    }
}