namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebuguserstate-enumeration
public enum CorDebugUserState {
    USER_STOP_REQUESTED = 1,
    USER_SUSPEND_REQUESTED = 2,
    USER_BACKGROUND = 4,
    USER_UNSTARTED = 8,
    USER_STOPPED = 0x10,
    USER_WAIT_SLEEP_JOIN = 0x20,
    USER_SUSPENDED = 0x40,
    USER_UNSAFE_POINT = 0x80,
    USER_THREADPOOL = 0x100
}