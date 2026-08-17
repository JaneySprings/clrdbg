using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugHandleValueExtensions {
    public static void Dispose(this ICorDebugHandleValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryDispose());
    }
}