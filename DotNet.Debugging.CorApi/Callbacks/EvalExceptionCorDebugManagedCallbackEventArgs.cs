namespace DotNet.Debugging.CorApi;

public sealed class EvalExceptionCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugThread Thread { get; }
    public ICorDebugEval Eval { get; }

    public EvalExceptionCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugEval pEval) {
        AppDomain = pAppDomain;
        Thread = pThread;
        Eval = pEval;
    }
}