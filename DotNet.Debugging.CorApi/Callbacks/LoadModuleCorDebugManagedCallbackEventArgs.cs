namespace DotNet.Debugging.CorApi;

public sealed class LoadModuleCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugModule Module { get; }

    public LoadModuleCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule) {
        AppDomain = pAppDomain;
        Module = pModule;
    }
}