using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugBoxValueExtensions {
    public static ICorDebugObjectValue GetObject(this ICorDebugBoxValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetObject(out var ppObject));
        return ppObject;
    }
}