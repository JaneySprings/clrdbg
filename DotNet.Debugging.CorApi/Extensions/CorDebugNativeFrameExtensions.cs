using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugNativeFrameExtensions {
    public static int GetIP(this ICorDebugNativeFrame instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetIP(out var pnOffset));
        return checked((int)pnOffset);
    }
}
