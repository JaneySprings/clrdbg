using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugArrayValueExtensions {
    public static uint[] GetDimensions(this ICorDebugArrayValue instance, int cdim) {
        var array = new uint[cdim];
        Marshal.ThrowExceptionForHR(instance.TryGetDimensions(checked((uint)cdim), array));
        return array;
    }

    public static int GetCount(this ICorDebugArrayValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetCount(out var pnCount));
        return checked((int)pnCount);
    }

    public static ICorDebugValue GetElement(this ICorDebugArrayValue instance, int cdim, uint[] indices) {
        Marshal.ThrowExceptionForHR(instance.TryGetElement(checked((uint)cdim), indices, out var ppValue));
        return ppValue;
    }

    public static ICorDebugValue GetElementAtPosition(this ICorDebugArrayValue instance, int nPosition) {
        Marshal.ThrowExceptionForHR(instance.TryGetElementAtPosition(checked((uint)nPosition), out var ppValue));
        return ppValue;
    }

    public static int GetRank(this ICorDebugArrayValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetRank(out var pnRank));
        return checked((int)pnRank);
    }

    public static void GetDimensions(this ICorDebugArrayValue instance, int cdim, uint[] dims) {
        Marshal.ThrowExceptionForHR(instance.TryGetDimensions(checked((uint)cdim), dims));
    }
}