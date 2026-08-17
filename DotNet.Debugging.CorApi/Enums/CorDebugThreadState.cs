namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugthreadstate-enumeration
public enum CorDebugThreadState {
    THREAD_RUN,
    THREAD_SUSPEND
}