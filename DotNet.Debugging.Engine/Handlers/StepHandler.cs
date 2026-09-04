using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine;

public partial class ManagedDebugger {
    private void HandleStepComplete(StepCompleteCorDebugManagedCallbackEventArgs callbackEvent) {
        // A step does not survive into an evaluation (a breakpoint that evaluates cancels it first), should one still
        // complete meanwhile it is dropped: a stop here would leave the evaluation waiting for a completion that never comes
        if (IsEvaluating) {
            stepController.CancelStep();
            ContinueProcess();
            return;
        }
        var thread = callbackEvent.Thread;
        if (!stepController.TryCompleteStep(thread, callbackEvent.Reason, out var location)) {
            ContinueProcess();
            return;
        }
        OnStopped?.Invoke(new StopInfo(thread.GetId(), StopReason.Step, location));
    }
    // Debugger.Break() in the debuggee
    private void HandleBreak(BreakCorDebugManagedCallbackEventArgs callbackEvent) {
        // Reached by the evaluated code, or by any other thread while an evaluation runs: like a breakpoint it cannot stop then
        if (IsEvaluating) {
            ContinueProcess();
            return;
        }
        stepController.Disable();
        OnStopped?.Invoke(new StopInfo(callbackEvent.Thread.GetId(), StopReason.Pause, GetSourceLocation(callbackEvent.Thread.GetActiveFrame())));
    }
}
