namespace DotNet.Debugging.CorApi;

public sealed class ControlCTrapCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs {
    public ICorDebugProcess Process { get; }

    public ControlCTrapCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess) {
        Process = pProcess;
    }
}