namespace DotNet.Debugging.CorApi;

public sealed class UpdateModuleSymbolsCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugModule Module { get; }
    public nint SymbolStream { get; }

    public UpdateModuleSymbolsCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule, nint pSymbolStream) {
        AppDomain = pAppDomain;
        Module = pModule;
        SymbolStream = pSymbolStream;
    }
}