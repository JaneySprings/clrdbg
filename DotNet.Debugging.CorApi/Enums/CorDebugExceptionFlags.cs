namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugexceptionflags-enumeration
public enum CorDebugExceptionFlags {
    DEBUG_EXCEPTION_NONE,
    DEBUG_EXCEPTION_CAN_BE_INTERCEPTED
}