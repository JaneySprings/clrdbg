using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugProcessExtensions {
    public static (byte[] buffer, nuint read) ReadMemory(this ICorDebugProcess instance, CordbAddress address, int size) {
        var array = new byte[size];
        Marshal.ThrowExceptionForHR(instance.TryReadMemory(address, checked((uint)size), array, out var read));
        return (buffer: array, read: read);
    }

    public static int GetId(this ICorDebugProcess instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetID(out var pdwProcessId));
        return checked((int)pdwProcessId);
    }
}
