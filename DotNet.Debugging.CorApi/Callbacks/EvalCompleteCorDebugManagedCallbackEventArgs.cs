namespace DotNet.Debugging.CorApi;

public sealed class EvalCompleteCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugThread Thread { get; }
    public ICorDebugEval Eval { get; }

    public EvalCompleteCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugEval pEval) {
        AppDomain = pAppDomain;
        Thread = pThread;
        Eval = pEval;
    }
}