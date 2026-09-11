using System.Text;
using System.Text.RegularExpressions;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Breakpoints;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Evaluation;
using DotNet.Debugging.Engine.Extensions;
using DotNet.Debugging.Engine.Logging;
using DotNet.Debugging.Engine.Models;
using DotNet.Debugging.Engine.Stepping;

namespace DotNet.Debugging.Engine;

public partial class ManagedDebugger {
    private static readonly Regex logPointExpressionRegex = new Regex(@"\{([^{}]+)\}", RegexOptions.Compiled);

    private async Task HandleBreakpointAsync(BreakpointCorDebugManagedCallbackEventArgs callbackEvent) {
        if (IsEvaluating) {
            ContinueProcess();
            return;
        }
        // A breakpoint at the step destination, hit by the stepping thread: the StepComplete callback is queued behind this
        // one and reports the stop. Another thread's breakpoint hit in the same instant is its own stop, the step is
        // abandoned like by any other breakpoint (its queued completion is dropped by HandleStepComplete)
        if (stepController.IsStepComplete && stepController.SteppingThreadId == callbackEvent.Thread.GetId()) {
            ContinueProcess();
            return;
        }
        if (callbackEvent.Breakpoint is not ICorDebugFunctionBreakpoint functionBreakpoint) {
            DebuggerLoggingService.LogMessage("Unknown breakpoint type hit");
            ContinueProcess();
            return;
        }

        var thread = callbackEvent.Thread;
        var asyncResult = await stepController.TryHandleBreakpointAsync(thread, functionBreakpoint);
        if (asyncResult == AsyncBreakpointResult.Continue) {
            ContinueProcess();
            return;
        }
        if (asyncResult == AsyncBreakpointResult.StepOut) {
            stepController.CancelStep();
            stepController.CreateStepper(thread, StepKind.Out);
            ContinueProcess();
            return;
        }
        if (TryHandleEntryPointBreakpoint(thread, functionBreakpoint))
            return;

        var breakpoint = breakpointManager.FindByCorBreakpoint(functionBreakpoint);
        if (breakpoint == null) {
            DebuggerLoggingService.LogMessage("A breakpoint unknown to the debugger was hit");
            ContinueProcess();
            return;
        }

        breakpoint.HitCount++;
        // A hit count that does not stop leaves an in-flight step alone, the step carries on past the breakpoint
        if (breakpoint.HitCondition != null && !breakpoint.HitCondition.MatchesHitCount(breakpoint.HitCount)) {
            DebuggerLoggingService.LogMessage($"Hit count condition not met: count={breakpoint.HitCount}, condition={breakpoint.HitCondition}");
            ContinueProcess();
            return;
        }
        // From here the breakpoint either stops or evaluates in the debuggee. A stop wins over a step in flight. An
        // evaluation runs the debuggee, and the step goes on afterwards when it does not stop: the stepping thread's
        // own stepper is suspended for it (the hijacked evaluation and the stepper's patches would share the thread)
        // and re-armed after; another thread's stepper stays armed, and a completion arriving during the evaluation
        // is held back until the breakpoint has decided (HandleStepComplete)
        var evaluates = breakpoint.Condition != null || breakpoint.LogMessage != null;
        if (evaluates && stepController.SteppingThreadId == thread.GetId())
            stepController.SuspendStep();
        if (breakpoint.Condition != null && !await EvaluateConditionAsync(thread, breakpoint.Condition)) {
            DebuggerLoggingService.LogMessage($"Breakpoint condition not met: {breakpoint.Condition}");
            ContinueAfterEvaluation(thread);
            return;
        }
        if (breakpoint.LogMessage != null) {
            OnLogPoint?.Invoke(await InterpolateLogMessageAsync(thread, breakpoint.LogMessage));
            ContinueAfterEvaluation(thread);
            return;
        }

        // The stop abandons what is left of the step, including an async step out that has no stepper and waits
        // for its task through the notification breakpoint: it must not fire after the user has moved on
        stepController.Disable();
        var location = breakpoint.ResolvedLocation?.Location ?? GetSourceLocation(thread.GetActiveFrame());
        OnStopped?.Invoke(new StopInfo(thread.GetId(), StopReason.Breakpoint, location, [breakpoint.Id]));
    }

    // Carries on after a breakpoint that evaluated without stopping: a step completion held back during the evaluation
    // is reported now (or its step resumed, when the completed step has to go on), a step suspended on the evaluating
    // thread is re-armed
    private void ContinueAfterEvaluation(ICorDebugThread thread) {
        if (stepController.TryTakeHeldCompletion(out var completedThread, out var reason)) {
            if (stepController.TryCompleteStep(completedThread, reason, out var location)) {
                OnStopped?.Invoke(new StopInfo(completedThread.GetId(), StopReason.Step, location));
                return;
            }
            ContinueProcess();
            return;
        }
        stepController.ResumeSuspendedStep(thread);
        ContinueProcess();
    }
    // The entry breakpoint is not tracked by the breakpoint manager, it is matched by identity or by exclusion
    private bool TryHandleEntryPointBreakpoint(ICorDebugThread thread, ICorDebugFunctionBreakpoint functionBreakpoint) {
        if (entryPointBreakpoint == null)
            return false;
        if (functionBreakpoint != entryPointBreakpoint && breakpointManager.FindByCorBreakpoint(functionBreakpoint) != null)
            return false;

        ClearEntryPointBreakpoint();
        stepController.Disable();
        OnStopped?.Invoke(new StopInfo(thread.GetId(), StopReason.Entry, GetSourceLocation(thread.GetActiveFrame())));
        return true;
    }
    // A condition that cannot be evaluated does not stop
    private async Task<bool> EvaluateConditionAsync(ICorDebugThread thread, string condition) {
        try {
            var context = new EvaluationContext(thread, thread.GetId(), 0);
            using var result = await GetEvaluator().EvaluateAsync(condition, context);
            if (result.Error != null) {
                DebuggerLoggingService.LogMessage($"Condition evaluation error for '{condition}': {result.Error}");
                return false;
            }
            return result.Value != null && CilValue.FromCorValue(result.Value).IsTrue();
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError($"Exception evaluating the condition '{condition}'", ex);
            return false;
        }
    }
    // Every '{expression}' of the message is replaced by its value in the top frame, an expression that fails is kept as is
    private async Task<string> InterpolateLogMessageAsync(ICorDebugThread thread, string message) {
        var result = new StringBuilder();
        var position = 0;
        foreach (Match match in logPointExpressionRegex.Matches(message)) {
            result.Append(message, position, match.Index - position);
            result.Append(await EvaluateLogExpressionAsync(thread, match.Groups[1].Value) ?? match.Value);
            position = match.Index + match.Length;
        }
        result.Append(message, position, message.Length - position);
        return result.ToString();
    }
    private async Task<string?> EvaluateLogExpressionAsync(ICorDebugThread thread, string expression) {
        try {
            var threadId = thread.GetId();
            var context = new EvaluationContext(thread, threadId, 0);
            using var result = await GetEvaluator().EvaluateAsync(expression, context);
            if (result.Error != null || result.Value == null) {
                DebuggerLoggingService.LogMessage($"Failed to evaluate the logpoint expression '{expression}': {result.Error}");
                return null;
            }
            var display = await variableProvider.FormatValueAsync(result.Value, threadId, 0, escapeStrings: true, createProxy: false);
            return display.Value;
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError($"Failed to evaluate the logpoint expression '{expression}'", ex);
            return null;
        }
    }
}
