namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugstatechange-enumeration
public enum CorDebugStateChange {
    PROCESS_RUNNING = 0x0000001,
    FLUSH_ALL
}