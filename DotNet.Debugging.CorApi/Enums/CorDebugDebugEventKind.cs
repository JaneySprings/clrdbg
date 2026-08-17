namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugdebugeventkind-enumeration
public enum CorDebugDebugEventKind {
    DEBUG_EVENT_KIND_MODULE_LOADED = 1,
    DEBUG_EVENT_KIND_MODULE_UNLOADED,
    DEBUG_EVENT_KIND_MANAGED_EXCEPTION_FIRST_CHANCE,
    DEBUG_EVENT_KIND_MANAGED_EXCEPTION_USER_FIRST_CHANCE,
    DEBUG_EVENT_KIND_MANAGED_EXCEPTION_CATCH_HANDLER_FOUND,
    DEBUG_EVENT_KIND_MANAGED_EXCEPTION_UNHANDLED
}