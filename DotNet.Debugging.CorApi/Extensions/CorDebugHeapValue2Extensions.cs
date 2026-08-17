using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugHeapValue2Extensions {
    public static ICorDebugHandleValue CreateHandle(this ICorDebugHeapValue2 instance, CorDebugHandleType type) {
        Marshal.ThrowExceptionForHR(instance.TryCreateHandle(type, out var ppHandle));
        return ppHandle;
    }
}