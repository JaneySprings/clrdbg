using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugModule2Extensions {
    public static void SetJMCStatus(this ICorDebugModule2 instance, bool bIsJustMyCode, int cTokens, MetadataToken[] pTokens) {
        Marshal.ThrowExceptionForHR(instance.TrySetJMCStatus(bIsJustMyCode, checked((uint)cTokens), pTokens));
    }
}