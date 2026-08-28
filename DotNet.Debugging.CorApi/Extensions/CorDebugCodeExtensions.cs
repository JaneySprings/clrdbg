using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugCodeExtensions {
    public static ICorDebugFunctionBreakpoint CreateBreakpoint(this ICorDebugCode instance, int offset) {
        Marshal.ThrowExceptionForHR(instance.TryCreateBreakpoint(checked((uint)offset), out var ppBreakpoint));
        return ppBreakpoint;
    }

    public static CordbAddress GetAddress(this ICorDebugCode instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetAddress(out var pStart));
        return pStart;
    }

    public static int GetSize(this ICorDebugCode instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetSize(out var pcBytes));
        return checked((int)pcBytes);
    }

    public static int GetVersionNumber(this ICorDebugCode instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetVersionNumber(out var nVersion));
        return checked((int)nVersion);
    }

    public static CorDebugIlToNativeMap[] GetILToNativeMapping(this ICorDebugCode instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetILToNativeMapping(0u, out var pcMap, null));
        if (pcMap == 0) {
            return Array.Empty<CorDebugIlToNativeMap>();
        }
        var map = new CorDebugIlToNativeMap[pcMap];
        Marshal.ThrowExceptionForHR(instance.TryGetILToNativeMapping(pcMap, out _, map));
        return map;
    }
}