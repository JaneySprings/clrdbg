namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebughandletype-enumeration
public enum CorDebugHandleType {
    HANDLE_STRONG = 1,
    HANDLE_WEAK_TRACK_RESURRECTION,
    HANDLE_PINNED
}