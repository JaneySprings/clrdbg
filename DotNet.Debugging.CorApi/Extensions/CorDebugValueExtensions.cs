using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugValueExtensions {
    public static CordbAddress GetAddress(this ICorDebugValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetAddress(out var pAddress));
        return pAddress;
    }

    public static int GetSize(this ICorDebugValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetSize(out var pSize));
        return checked((int)pSize);
    }

    public static CorElementType GetElementType(this ICorDebugValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetType(out var pType));
        return pType;
    }
}