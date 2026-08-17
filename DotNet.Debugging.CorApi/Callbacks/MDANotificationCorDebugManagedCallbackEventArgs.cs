namespace DotNet.Debugging.CorApi;

public sealed class MDANotificationCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugController Controller { get; }
    public ICorDebugThread Thread { get; }
    public ICorDebugMDA MDA { get; }

    public MDANotificationCorDebugManagedCallbackEventArgs(ICorDebugController pController, ICorDebugThread pThread, ICorDebugMDA pMDA) {
        Controller = pController;
        Thread = pThread;
        MDA = pMDA;
    }
}