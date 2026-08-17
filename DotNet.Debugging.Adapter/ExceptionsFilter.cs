using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

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

public class ExceptionFilterOptions {
    public bool Enabled { get; private set; }
    private readonly List<string> includedTypes = new List<string>();
    private readonly List<string> excludedTypes = new List<string>();

    public void Reset() {
        Enabled = false;
        includedTypes.Clear();
        excludedTypes.Clear();
    }
    public void Enable(string? condition = null) {
        Enabled = true;
        if (string.IsNullOrEmpty(condition))
            return;

        if (condition.StartsWith('!')) {
            foreach (var exceptionType in condition.Substring(1).Split(',', StringSplitOptions.RemoveEmptyEntries))
                excludedTypes.Add(exceptionType.Trim());
        }
        else {
            foreach (var exceptionType in condition.Split(',', StringSplitOptions.RemoveEmptyEntries))
                includedTypes.Add(exceptionType.Trim());
        }
    }
    public bool ShouldStopOnException(string? typeName) {
        if (!Enabled)
            return false;
        if (string.IsNullOrEmpty(typeName))
            return true;
        if (includedTypes.Count > 0 && !includedTypes.Contains(typeName))
            return false;
        if (excludedTypes.Contains(typeName))
            return false;

        return true;
    }
}