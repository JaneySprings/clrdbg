namespace DotNet.Debugging.Engine.Logging;

public static class DebuggerLoggingService {
    public static ICustomLogger? CustomLogger { get; set; }
    public static event Action<string>? OnEngineMessage;

    internal static void LogMessage(string message) {
        CustomLogger?.LogMessage(message);
        OnEngineMessage?.Invoke(message);
    }
    internal static void LogError(string message, Exception? exception = null) {
        CustomLogger?.LogError(message, exception);
        OnEngineMessage?.Invoke(message);
    }
}
