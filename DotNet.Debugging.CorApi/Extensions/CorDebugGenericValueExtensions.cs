using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugGenericValueExtensions {
    public static void GetValue(this ICorDebugGenericValue instance, nint pTo) {
        Marshal.ThrowExceptionForHR(instance.TryGetValue(pTo));
    }

    public static void SetValue(this ICorDebugGenericValue instance, nint pFrom) {
        Marshal.ThrowExceptionForHR(instance.TrySetValue(pFrom));
    }
}