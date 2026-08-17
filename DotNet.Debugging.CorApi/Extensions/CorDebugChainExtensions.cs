using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugChainExtensions {
    public static IEnumerable<ICorDebugFrame> EnumerateFrames(this ICorDebugChain instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateFrames(out var ppFrames));
        return EnumerateFramesCore(ppFrames);
    }

    public static ICorDebugThread GetThread(this ICorDebugChain instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetThread(out var ppThread));
        return ppThread;
    }

    public static bool IsManaged(this ICorDebugChain instance) {
        Marshal.ThrowExceptionForHR(instance.TryIsManaged(out var pManaged));
        return pManaged;
    }

    private static IEnumerable<ICorDebugFrame> EnumerateFramesCore(ICorDebugFrameEnum enumerator) {
        while (true) {
            var array = new ICorDebugFrame[1];
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