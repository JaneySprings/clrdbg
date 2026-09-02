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

    public static ICorDebugValue GetElement(this ICorDebugArrayValue instance, uint[] indices) {
        Marshal.ThrowExceptionForHR(instance.TryGetElement((uint)indices.Length, indices, out var ppValue));
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

    public static bool HasBaseIndicies(this ICorDebugArrayValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryHasBaseIndicies(out var pbHasBaseIndicies));
        return pbHasBaseIndicies;
    }

    public static uint[] GetBaseIndicies(this ICorDebugArrayValue instance, int cdim) {
        var array = new uint[cdim];
        Marshal.ThrowExceptionForHR(instance.TryGetBaseIndicies(checked((uint)cdim), array));
        return array;
    }
}
