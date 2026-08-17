using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugProcessExtensions {
    public static IEnumerable<ICorDebugAppDomain> EnumerateAppDomains(this ICorDebugProcess instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateAppDomains(out var ppAppDomains));
        return EnumerateAppDomainsCore(ppAppDomains);
    }

    public static (byte[] buffer, nuint read) ReadMemory(this ICorDebugProcess instance, CordbAddress address, int size) {
        var array = new byte[size];
        Marshal.ThrowExceptionForHR(instance.TryReadMemory(address, checked((uint)size), array, out var read));
        return (buffer: array, read: read);
    }

    public static int GetId(this ICorDebugProcess instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetID(out var pdwProcessId));
        return checked((int)pdwProcessId);
    }

    public static ICorDebugThread GetThread(this ICorDebugProcess instance, int dwThreadId) {
        Marshal.ThrowExceptionForHR(instance.TryGetThread(checked((uint)dwThreadId), out var ppThread));
        return ppThread;
    }

    private static IEnumerable<ICorDebugAppDomain> EnumerateAppDomainsCore(ICorDebugAppDomainEnum enumerator) {
        while (true) {
            var array = new ICorDebugAppDomain[1];
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