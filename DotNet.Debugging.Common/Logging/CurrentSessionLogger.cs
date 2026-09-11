#if LOGGING
using NLog;
#endif

namespace DotNet.Debugging.Common.Logging;

public static class CurrentSessionLogger {
#if LOGGING
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    static CurrentSessionLogger() {
        LogConfig.InitializeLog();
    }
#endif

    public static void Error(Exception e) {
#if LOGGING
        logger.Error(e.ToString());
#endif
    }
    public static void Error(string message) {
#if LOGGING
        logger.Error(message);
#endif
    }
    public static void Debug(string message) {
#if LOGGING
        logger.Debug(message);
#endif
    }
}