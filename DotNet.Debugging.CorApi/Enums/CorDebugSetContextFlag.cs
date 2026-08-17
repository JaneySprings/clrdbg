namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugsetcontextflag-enumeration
public enum CorDebugSetContextFlag {
    SET_CONTEXT_FLAG_ACTIVE_FRAME = 1,
    SET_CONTEXT_FLAG_UNWIND_FRAME
}