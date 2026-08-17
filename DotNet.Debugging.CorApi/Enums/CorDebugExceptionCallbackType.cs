namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugexceptioncallbacktype-enumeration
public enum CorDebugExceptionCallbackType {
    DEBUG_EXCEPTION_FIRST_CHANCE = 1,
    DEBUG_EXCEPTION_USER_FIRST_CHANCE,
    DEBUG_EXCEPTION_CATCH_HANDLER_FOUND,
    DEBUG_EXCEPTION_UNHANDLED
}