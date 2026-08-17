namespace DotNet.Debugging.CorApi;

public sealed class ExitAppDomainCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugProcess Process { get; }
    public ICorDebugAppDomain AppDomain { get; }

    public ExitAppDomainCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess, ICorDebugAppDomain pAppDomain) {
        Process = pProcess;
        AppDomain = pAppDomain;
    }
}