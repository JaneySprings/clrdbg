using DotNet.Debugging.Common.Logging;
using DotNet.Debugging.Engine.Logging;

namespace DotNet.Debugging.Adapter.Logging;

public class EngineLogger : ICustomLogger {
    private readonly CurrentClassLogger logger = new CurrentClassLogger("CorDebug");

    public void LogMessage(string message) => logger.Debug(message);
    public void LogError(string message, Exception? exception) => logger.Error(exception == null ? message : $"{message}: {exception}");
}
