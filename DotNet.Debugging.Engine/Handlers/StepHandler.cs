using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine;

public partial class ManagedDebugger {
    private void HandleStepComplete(StepCompleteCorDebugManagedCallbackEventArgs callbackEvent) {
        var thread = callbackEvent.Thread;
        // The step was abandoned (a stop on another thread, an exception the user kept) and the user has moved on since. Its
        // completion can still arrive after the user's next step: queued behind the other thread's breakpoint, the next
        // continue dispatches it, by which time a new stepper may already be in progress on the other thread
        if (!stepController.OwnsStepper(callbackEvent.Stepper)) {
            ContinueProcess();
            return;
        }
        // A step completing while a breakpoint on another thread evaluates its condition or logpoint: no stop is possible
        // until the evaluation is over, and the debuggee has to run for that. The completed thread is held where it stands
        // and the completion reported once the breakpoint has decided not to stop (BreakpointHandler). A stepper on the
        // evaluating thread itself is suspended before the evaluation; should one complete there anyway it is dropped,
        // holding that thread would wedge the evaluation
        if (IsEvaluating) {
            if (FuncEval.RunningThreadId == thread.GetId() || !stepController.TryHoldCompletion(thread, callbackEvent.Reason))
                stepController.CancelStep();
            ContinueProcess();
            return;
        }
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
