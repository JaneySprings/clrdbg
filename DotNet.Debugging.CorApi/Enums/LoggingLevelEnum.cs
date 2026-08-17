namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/logginglevelenum-enumeration
public enum LoggingLevelEnum {
    LTraceLevel0 = 0,
    LTraceLevel1 = 1,
    LTraceLevel2 = 2,
    LTraceLevel3 = 3,
    LTraceLevel4 = 4,
    LStatusLevel0 = 20,
    LStatusLevel1 = 21,
    LStatusLevel2 = 22,
    LStatusLevel3 = 23,
    LStatusLevel4 = 24,
    LWarningLevel = 40,
    LErrorLevel = 50,
    LPanicLevel = 100
}