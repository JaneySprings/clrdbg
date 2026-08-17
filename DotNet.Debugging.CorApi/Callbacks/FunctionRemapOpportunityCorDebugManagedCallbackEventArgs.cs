namespace DotNet.Debugging.CorApi;

public sealed class FunctionRemapOpportunityCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugThread Thread { get; }
    public ICorDebugFunction OldFunction { get; }
    public ICorDebugFunction NewFunction { get; }
    public uint OldILOffset { get; }

    public FunctionRemapOpportunityCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pOldFunction, ICorDebugFunction pNewFunction, uint oldILOffset) {
        AppDomain = pAppDomain;
        Thread = pThread;
        OldFunction = pOldFunction;
        NewFunction = pNewFunction;
        OldILOffset = oldILOffset;
    }
}