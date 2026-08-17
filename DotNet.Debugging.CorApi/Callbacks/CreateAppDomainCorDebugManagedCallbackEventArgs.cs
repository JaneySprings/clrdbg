namespace DotNet.Debugging.CorApi;

public sealed class CreateAppDomainCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugProcess Process { get; }
    public ICorDebugAppDomain AppDomain { get; }

    public CreateAppDomainCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess, ICorDebugAppDomain pAppDomain) {
        Process = pProcess;
        AppDomain = pAppDomain;
    }
}