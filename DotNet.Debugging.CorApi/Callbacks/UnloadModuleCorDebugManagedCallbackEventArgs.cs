namespace DotNet.Debugging.CorApi;

public sealed class UnloadModuleCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugModule Module { get; }

    public UnloadModuleCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule) {
        AppDomain = pAppDomain;
        Module = pModule;
    }
}