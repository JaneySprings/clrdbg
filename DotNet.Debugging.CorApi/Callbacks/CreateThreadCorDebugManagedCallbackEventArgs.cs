namespace DotNet.Debugging.CorApi;

public sealed class CreateThreadCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugThread Thread { get; }

    public CreateThreadCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread thread) {
        AppDomain = pAppDomain;
        Thread = thread;
    }
}