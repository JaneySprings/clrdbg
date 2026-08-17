using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugReferenceValueExtensions {
    public static ICorDebugValue Dereference(this ICorDebugReferenceValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryDereference(out var ppValue));
        return ppValue;
    }

    public static CordbAddress GetValue(this ICorDebugReferenceValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetValue(out var pValue));
        return pValue;
    }

    public static void SetValue(this ICorDebugReferenceValue instance, CordbAddress value) {
        Marshal.ThrowExceptionForHR(instance.TrySetValue(value));
    }

    public static bool IsNull(this ICorDebugReferenceValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryIsNull(out var pbNull));
        return pbNull;
    }
}