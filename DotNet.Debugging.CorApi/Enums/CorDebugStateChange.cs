namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugstatechange-enumeration
public enum CorDebugStateChange {
    PROCESS_RUNNING = 1,
    FLUSH_ALL
}