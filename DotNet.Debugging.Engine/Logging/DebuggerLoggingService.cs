namespace DotNet.Debugging.Engine.Logging;

// The engine logs through the logger the host sets here. Nothing is written when no logger is set:
// a debug adapter owns the standard streams, so there is no safe default sink
public static class DebuggerLoggingService {
    public static ICustomLogger? CustomLogger { get; set; }

    public static void LogMessage(string message) {
        CustomLogger?.LogMessage(message);
    }
    public static void LogError(string message, Exception? exception = null) {
        CustomLogger?.LogError(message, exception);
    }
}
