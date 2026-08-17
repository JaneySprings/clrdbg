namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugchainreason-enumeration
public enum CorDebugChainReason {
    CHAIN_NONE = 0,
    CHAIN_CLASS_INIT = 1,
    CHAIN_EXCEPTION_FILTER = 2,
    CHAIN_SECURITY = 4,
    CHAIN_CONTEXT_POLICY = 8,
    CHAIN_INTERCEPTION = 0x10,
    CHAIN_PROCESS_START = 0x20,
    CHAIN_THREAD_START = 0x40,
    CHAIN_ENTER_MANAGED = 0x80,
    CHAIN_ENTER_UNMANAGED = 0x100,
    CHAIN_DEBUGGER_EVAL = 0x200,
    CHAIN_CONTEXT_SWITCH = 0x400,
    CHAIN_FUNC_EVAL = 0x800
}