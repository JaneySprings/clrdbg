using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugModuleModule2Extensions {
    public static ICorDebugModule2 GetModule2(this ICorDebugModule instance) => (instance as ICorDebugModule2) ?? throw new NotSupportedException("ICorDebugModule does not support ICorDebugModule2.");

    public static CorDebugJITCompilerFlags GetJITCompilerFlags(this ICorDebugModule instance) {
        Marshal.ThrowExceptionForHR(instance.GetModule2().TryGetJITCompilerFlags(out var pdwFlags));
        return pdwFlags;
    }

    public static void SetJMCStatus(this ICorDebugModule instance, bool bIsJustMyCode, MetadataToken[] pTokens) {
        instance.GetModule2().SetJMCStatus(bIsJustMyCode, pTokens);
    }
}
