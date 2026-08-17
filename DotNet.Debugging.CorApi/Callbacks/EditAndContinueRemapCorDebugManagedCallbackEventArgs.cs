namespace DotNet.Debugging.CorApi;

public sealed class EditAndContinueRemapCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugThread Thread { get; }
    public ICorDebugFunction Function { get; }
    public bool FAccurate { get; }

    public EditAndContinueRemapCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pFunction, bool fAccurate) {
        AppDomain = pAppDomain;
        Thread = pThread;
        Function = pFunction;
        FAccurate = fAccurate;
    }
}