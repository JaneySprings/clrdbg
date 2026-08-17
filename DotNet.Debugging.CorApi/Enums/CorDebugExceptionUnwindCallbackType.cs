namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugexceptionunwindcallbacktype-enumeration
public enum CorDebugExceptionUnwindCallbackType {
    DEBUG_EXCEPTION_UNWIND_BEGIN = 1,
    DEBUG_EXCEPTION_INTERCEPTED
}