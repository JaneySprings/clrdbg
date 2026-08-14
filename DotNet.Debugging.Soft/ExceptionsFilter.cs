using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft.Extensions;

public static class ExceptionsFilter {
    public static ExceptionBreakpointsFilter AllExceptions { get; } = new ExceptionBreakpointsFilter {
        Filter = "all",
        Label = "All Exceptions",
        Description = "Break when an exception is thrown. For more information about exception settings, see: https://aka.ms/VSCode-CS-ExceptionSettings",
        ConditionDescription = "Comma-separated list of exception types to break on, or if the list starts with '!', a list of exception types to ignore.",
        SupportsCondition = true
    };
    public static ExceptionBreakpointsFilter UserUnhandledExceptions { get; } = new ExceptionBreakpointsFilter {
        Filter = "userUnhandled",
        Label = "User-Unhandled Exceptions",
        Description = "Break when an exception is caught in non-user code (system code) after having passed through user code. For more information about exception settings, see: https://aka.ms/VSCode-CS-ExceptionSettings",
        ConditionDescription = "Comma-separated list of exception types to break on, or if the list starts with '!', a list of exception types to ignore.",
        SupportsCondition = true
    };
}