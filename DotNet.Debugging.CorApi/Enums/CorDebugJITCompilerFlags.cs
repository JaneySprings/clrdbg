namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugjitcompilerflags-enumeration
public enum CorDebugJITCompilerFlags {
    CORDEBUG_JIT_DEFAULT = 0x1,
    CORDEBUG_JIT_DISABLE_OPTIMIZATION = 0x3,
    CORDEBUG_JIT_ENABLE_ENC = 0x7
}