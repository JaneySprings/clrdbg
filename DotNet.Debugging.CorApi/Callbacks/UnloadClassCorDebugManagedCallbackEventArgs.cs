namespace DotNet.Debugging.CorApi;

public sealed class UnloadClassCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugClass C { get; }

    public UnloadClassCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugClass c) {
        AppDomain = pAppDomain;
        C = c;
    }
}