using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Logging;
using DotNet.Debugging.Engine.Models;
using DotNet.Debugging.Engine.Variables;

namespace DotNet.Debugging.Engine;

public partial class ManagedDebugger {
    // First-chance and unhandled exceptions. The subscriber applies its filters and continues when the exception is not interesting
    private void HandleException(ExceptionCorDebugManagedCallbackEventArgs callbackEvent) {
        if (IsEvaluating || OnExceptionThrown == null) {
            ContinueProcess();
            return;
        }

        var threadId = callbackEvent.Thread.GetId();
        stepController.Disable();
        if (callbackEvent.Unhandled)
            exceptionThreads.Remove(threadId);
        var kind = callbackEvent.Unhandled ? ExceptionStopKind.Unhandled : ExceptionStopKind.FirstChance;
        OnExceptionThrown.Invoke(new ExceptionStopInfo(threadId, kind, GetExceptionTypeName(threadId)));
    }
    // Follows the exception dispatch to detect 'user-unhandled' exceptions: an exception that passed through
    // user code and is about to be caught in non-user code
    private void HandleExceptionDispatch(Exception2CorDebugManagedCallbackEventArgs callbackEvent) {
        if (IsEvaluating) {
            ContinueProcess();
            return;
        }

        var threadId = callbackEvent.Thread.GetId();
        switch (callbackEvent.DwEventType) {
            case CorDebugExceptionCallbackType.DEBUG_EXCEPTION_FIRST_CHANCE:
            case CorDebugExceptionCallbackType.DEBUG_EXCEPTION_USER_FIRST_CHANCE:
                if (callbackEvent.DwEventType == CorDebugExceptionCallbackType.DEBUG_EXCEPTION_USER_FIRST_CHANCE || IsUserCodeFrame(callbackEvent.Frame))
                    exceptionThreads.Add(threadId);
                break;
            case CorDebugExceptionCallbackType.DEBUG_EXCEPTION_CATCH_HANDLER_FOUND:
                var passedThroughUserCode = exceptionThreads.Remove(threadId);
                if (passedThroughUserCode && !IsUserCodeFrame(callbackEvent.Frame) && OnExceptionThrown != null) {
                    stepController.Disable();
                    OnExceptionThrown.Invoke(new ExceptionStopInfo(threadId, ExceptionStopKind.UserUnhandled, GetExceptionTypeName(threadId)));
                    return;
                }
                break;
        }
        ContinueProcess();
    }

    private bool IsUserCodeFrame(ICorDebugFrame? frame) {
        try {
            if (frame is not ICorDebugILFrame ilFrame)
                return false;
            var module = FindModule(ilFrame.GetFunction().GetModule());
            return module != null && module.IsUserCode;
        }
        catch {
            return false;
        }
    }
    private string? GetExceptionTypeName(int threadId) {
        try {
            var exception = GetCurrentException(threadId);
            return exception == null ? null : ValueFormatter.Format(exception, false).TypeName;
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError("Failed to get the current exception type", ex);
            return null;
        }
    }
}
