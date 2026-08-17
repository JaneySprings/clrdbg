using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugILFrame2Extensions {
    public static IEnumerable<ICorDebugType> EnumerateTypeParameters(this ICorDebugILFrame2 instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateTypeParameters(out var ppTyParEnum));
        return EnumerateTypeParametersCore(ppTyParEnum);
    }

    private static IEnumerable<ICorDebugType> EnumerateTypeParametersCore(ICorDebugTypeEnum enumerator) {
        while (true) {
            var array = new ICorDebugType[1];
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