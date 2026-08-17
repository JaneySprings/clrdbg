namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugstepreason-enumeration
public enum CorDebugStepReason {
    STEP_NORMAL,
    STEP_RETURN,
    STEP_CALL,
    STEP_EXCEPTION_FILTER,
    STEP_EXCEPTION_HANDLER,
    STEP_INTERCEPT,
    STEP_EXIT
}