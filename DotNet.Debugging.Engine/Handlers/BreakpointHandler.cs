using System.Text;
using System.Text.RegularExpressions;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Breakpoints;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Evaluation;
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
        // A breakpoint at the step destination: the StepComplete callback is queued behind this one and reports the stop
        if (stepController.IsStepping && stepController.IsStepComplete) {
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
        if (breakpoint.HitCondition != null && !BreakpointManager.CheckHitCondition(breakpoint.HitCount, breakpoint.HitCondition)) {
            DebuggerLoggingService.LogMessage($"Hit count condition not met: count={breakpoint.HitCount}, condition={breakpoint.HitCondition}");
            ContinueProcess();
            return;
        }
        // From here the breakpoint either stops or evaluates in the debuggee, neither of which a step survives:
        // a breakpoint that stops wins over the step, and an evaluation would have the stepper complete inside
        // the evaluated code (HandleStepComplete does not expect a completion while an evaluation runs)
        stepController.CancelStep();
        if (breakpoint.Condition != null && !await EvaluateConditionAsync(thread, breakpoint.Condition)) {
            DebuggerLoggingService.LogMessage($"Breakpoint condition not met: {breakpoint.Condition}");
            ContinueProcess();
            return;
        }
        if (breakpoint.LogMessage != null) {
            OnLogPoint?.Invoke(await InterpolateLogMessageAsync(thread, breakpoint.LogMessage));
            ContinueProcess();
            return;
        }

        var location = breakpoint.ResolvedLocation?.Location ?? GetSourceLocation(thread.GetActiveFrame());
        OnStopped?.Invoke(new StopInfo(thread.GetId(), StopReason.Breakpoint, location, [breakpoint.Id]));
    }

    // The entry breakpoint is not tracked by the breakpoint manager, it is matched by identity or by exclusion
    private bool TryHandleEntryPointBreakpoint(ICorDebugThread thread, ICorDebugFunctionBreakpoint functionBreakpoint) {
        if (entryPointBreakpoint == null)
            return false;
        if (functionBreakpoint != entryPointBreakpoint && breakpointManager.FindByCorBreakpoint(functionBreakpoint) != null)
            return false;

        ClearEntryPointBreakpoint();
        stepController.CancelStep();
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
            var display = await variableProvider.FormatValueAsync(result.Value, threadId, 0, escapeStrings: true);
            return display.Value;
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError($"Failed to evaluate the logpoint expression '{expression}'", ex);
            return null;
        }
    }
}
