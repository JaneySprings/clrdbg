using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugCodeExtensions {
    public static ICorDebugFunctionBreakpoint CreateBreakpoint(this ICorDebugCode instance, int offset) {
        Marshal.ThrowExceptionForHR(instance.TryCreateBreakpoint(checked((uint)offset), out var ppBreakpoint));
        return ppBreakpoint;
    }

    public static int GetSize(this ICorDebugCode instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetSize(out var pcBytes));
        return checked((int)pcBytes);
    }

    public static int GetVersionNumber(this ICorDebugCode instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetVersionNumber(out var nVersion));
        return checked((int)nVersion);
    }
}