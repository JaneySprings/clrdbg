namespace DotNet.Debugging.CorApi;

public sealed class StepCompleteCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugAppDomain AppDomain { get; }
    public ICorDebugThread Thread { get; }
    public ICorDebugStepper Stepper { get; }
    public CorDebugStepReason Reason { get; }

    public StepCompleteCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugStepper pStepper, CorDebugStepReason reason) {
        AppDomain = pAppDomain;
        Thread = pThread;
        Stepper = pStepper;
        Reason = reason;
    }
}