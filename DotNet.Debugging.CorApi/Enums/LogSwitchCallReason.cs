namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/logswitchcallreason-enumeration
public enum LogSwitchCallReason {
    SWITCH_CREATE,
    SWITCH_MODIFY,
    SWITCH_DELETE
}