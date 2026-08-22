namespace DotNet.Debugging.Engine.Logging;

public interface ICustomLogger {
    void LogMessage(string message);
    void LogError(string message, Exception? exception);
}
