using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugExceptionObjectValueExtensions {
    public static IEnumerable<CorDebugExceptionObjectStackFrame> EnumerateExceptionCallStack(this ICorDebugExceptionObjectValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateExceptionCallStack(out var ppCallStackEnum));
        return EnumerateExceptionCallStackCore(ppCallStackEnum);
    }

    private static IEnumerable<CorDebugExceptionObjectStackFrame> EnumerateExceptionCallStackCore(ICorDebugExceptionObjectCallStackEnum enumerator) {
        while (true) {
            var array = new CorDebugExceptionObjectStackFrame[1];
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
