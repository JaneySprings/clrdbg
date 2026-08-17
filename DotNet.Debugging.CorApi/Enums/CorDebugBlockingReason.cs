namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugblockingreason-enumeration
public enum CorDebugBlockingReason {
    BLOCKING_NONE,
    BLOCKING_MONITOR_CRITICAL_SECTION,
    BLOCKING_MONITOR_EVENT
}