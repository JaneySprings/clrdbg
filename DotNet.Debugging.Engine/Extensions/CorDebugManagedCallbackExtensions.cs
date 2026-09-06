using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;

namespace DotNet.Debugging.Engine.Extensions;

internal static class CorDebugManagedCallbackExtensions {
    // The callback and what tells its occurrences apart: the thread, an exception dispatch's stage and frame, a step's reason
    public static string Describe(this CorDebugManagedCallbackEventArgs callbackEvent) {
        try {
            switch (callbackEvent) {
                case BreakpointCorDebugManagedCallbackEventArgs breakpoint:
                    return $"Breakpoint on thread {breakpoint.Thread.GetId()} at {breakpoint.Thread.GetActiveFrame().Describe()}";
                case StepCompleteCorDebugManagedCallbackEventArgs stepComplete:
                    return $"StepComplete ({stepComplete.Reason}) on thread {stepComplete.Thread.GetId()} at {stepComplete.Thread.GetActiveFrame().Describe()}";
                case ExceptionCorDebugManagedCallbackEventArgs exception:
                    return $"Exception ({(exception.Unhandled ? "unhandled" : "first chance")}) on thread {exception.Thread.GetId()} at {exception.Thread.GetActiveFrame().Describe()}";
                case Exception2CorDebugManagedCallbackEventArgs dispatch:
                    return $"Exception2 ({dispatch.DwEventType}) on thread {dispatch.Thread.GetId()}, frame {dispatch.Frame.Describe()}, offset IL_{dispatch.NOffset:X4}";
                case EvalCompleteCorDebugManagedCallbackEventArgs evalComplete:
                    return $"EvalComplete on thread {evalComplete.Thread.GetId()}";
                case EvalExceptionCorDebugManagedCallbackEventArgs evalException:
                    return $"EvalException on thread {evalException.Thread.GetId()}";
                default:
                    return callbackEvent.GetType().Name;
            }
        }
        catch {
            return callbackEvent.GetType().Name;
        }
    }
}
