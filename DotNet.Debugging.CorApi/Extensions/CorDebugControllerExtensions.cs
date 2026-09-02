using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugControllerExtensions {
    public static ICorDebugThread[] GetThreads(this ICorDebugController instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateThreads(out var threads));
        return threads.ToArray<ICorDebugThread>(threads.TryNext);
    }

    public static bool IsRunning(this ICorDebugController instance) {
        Marshal.ThrowExceptionForHR(instance.TryIsRunning(out var pbRunning));
        return pbRunning;
    }

    public static void Continue(this ICorDebugController instance, bool fIsOutOfBand) {
        Marshal.ThrowExceptionForHR(instance.TryContinue(fIsOutOfBand));
    }

    public static void Stop(this ICorDebugController instance, int dwTimeoutIgnored) {
        Marshal.ThrowExceptionForHR(instance.TryStop(checked((uint)dwTimeoutIgnored)));
    }

    public static void Terminate(this ICorDebugController instance, int exitCode) {
        Marshal.ThrowExceptionForHR(instance.TryTerminate(checked((uint)exitCode)));
    }
}
