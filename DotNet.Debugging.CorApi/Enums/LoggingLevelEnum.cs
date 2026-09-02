namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/logginglevelenum-enumeration
public enum LoggingLevelEnum {
    LTraceLevel0 = 0,
    LTraceLevel1 = LTraceLevel0 + 1,
    LTraceLevel2 = LTraceLevel1 + 1,
    LTraceLevel3 = LTraceLevel2 + 1,
    LTraceLevel4 = LTraceLevel3 + 1,
    LStatusLevel0 = 20,
    LStatusLevel1 = LStatusLevel0 + 1,
    LStatusLevel2 = LStatusLevel1 + 1,
    LStatusLevel3 = LStatusLevel2 + 1,
    LStatusLevel4 = LStatusLevel3 + 1,
    LWarningLevel = 40,
    LErrorLevel = 50,
    LPanicLevel = 100
}