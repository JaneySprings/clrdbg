using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugExtensions {
    public static ICorDebugProcess DebugActiveProcess(this ICorDebug instance, int id, bool win32Attach) {
        Marshal.ThrowExceptionForHR(instance.TryDebugActiveProcess(checked((uint)id), win32Attach, out var ppProcess));
        return ppProcess;
    }

    public static void Initialize(this ICorDebug instance) {
        Marshal.ThrowExceptionForHR(instance.TryInitialize());
    }

    public static void SetManagedHandler(this ICorDebug instance, ICorDebugManagedCallback pCallback) {
        Marshal.ThrowExceptionForHR(instance.TrySetManagedHandler(pCallback));
    }
}