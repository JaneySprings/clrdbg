namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugcodeinvokepurpose-enumeration
public enum CorDebugCodeInvokePurpose {
    CODE_INVOKE_PURPOSE_NONE,
    CODE_INVOKE_PURPOSE_NATIVE_TO_MANAGED_TRANSITION,
    CODE_INVOKE_PURPOSE_CLASS_INIT,
    CODE_INVOKE_PURPOSE_INTERFACE_DISPATCH
}