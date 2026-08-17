namespace DotNet.Debugging.CorApi;

public sealed class UnloadAssemblyCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugAssembly Assembly { get; }

    public UnloadAssemblyCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugAssembly pAssembly) {
        AppDomain = pAppDomain;
        Assembly = pAssembly;
    }
}