using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugThreadExtensions {
    public static IEnumerable<ICorDebugChain> EnumerateChains(this ICorDebugThread instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateChains(out var ppChains));
        return EnumerateChainsCore(ppChains);
    }

    public static ICorDebugEval CreateEval(this ICorDebugThread instance) {
        Marshal.ThrowExceptionForHR(instance.TryCreateEval(out var ppEval));
        return ppEval;
    }

    public static ICorDebugFrame GetActiveFrame(this ICorDebugThread instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetActiveFrame(out var ppFrame));
        return ppFrame;
    }

    public static ICorDebugAppDomain GetAppDomain(this ICorDebugThread instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetAppDomain(out var ppAppDomain));
        return ppAppDomain;
    }

    public static int GetId(this ICorDebugThread instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetID(out var pdwThreadId));
        return checked((int)pdwThreadId);
    }

    public static ICorDebugValue GetObject(this ICorDebugThread instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetObject(out var ppObject));
        return ppObject;
    }

    public static ICorDebugProcess GetProcess(this ICorDebugThread instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetProcess(out var ppProcess));
        return ppProcess;
    }

    private static IEnumerable<ICorDebugChain> EnumerateChainsCore(ICorDebugChainEnum enumerator) {
        while (true) {
            var array = new ICorDebugChain[1];
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