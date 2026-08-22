using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine;

public partial class ManagedDebugger {
    private void HandleStepComplete(StepCompleteCorDebugManagedCallbackEventArgs callbackEvent) {
        var thread = callbackEvent.Thread;
        if (!stepController.TryCompleteStep(thread, callbackEvent.Reason, out var location)) {
            ContinueProcess();
            return;
        }
        OnStopped?.Invoke(new StopInfo(thread.GetId(), StopReason.Step, location));
    }
    // Debugger.Break() in the debuggee
    private void HandleBreak(BreakCorDebugManagedCallbackEventArgs callbackEvent) {
        stepController.Disable();
        OnStopped?.Invoke(new StopInfo(callbackEvent.Thread.GetId(), StopReason.Pause, GetSourceLocation(callbackEvent.Thread.GetActiveFrame())));
    }
}
