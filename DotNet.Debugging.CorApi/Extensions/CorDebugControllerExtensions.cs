using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugControllerExtensions {
    public static IEnumerable<ICorDebugThread> EnumerateThreads(this ICorDebugController instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateThreads(out var ppThreads));
        return EnumerateThreadsCore(ppThreads);
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

    private static IEnumerable<ICorDebugThread> EnumerateThreadsCore(ICorDebugThreadEnum enumerator) {
        while (true) {
            var array = new ICorDebugThread[1];
            var errorCode = enumerator.TryNext(1u, array, out var pceltFetched);
            if (pceltFetched == 0) {
                yield break;
            }
            Marshal.ThrowExceptionForHR(errorCode);
            if (pceltFetched != 1) {
                break;
            }
            yield return array[0];
        }
        throw new InvalidOperationException("Native debugger enumerator returned an invalid item count.");
    }
}