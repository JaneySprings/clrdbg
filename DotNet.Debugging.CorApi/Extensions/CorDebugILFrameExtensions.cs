using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugILFrameExtensions {
    public static IEnumerable<ICorDebugValue> EnumerateArguments(this ICorDebugILFrame instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateArguments(out var ppValueEnum));
        return EnumerateArgumentsCore(ppValueEnum);
    }

    public static IEnumerable<ICorDebugValue> EnumerateLocalVariables(this ICorDebugILFrame instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateLocalVariables(out var ppValueEnum));
        return EnumerateLocalVariablesCore(ppValueEnum);
    }

    public static ICorDebugValue[] GetArguments(this ICorDebugILFrame instance) => instance.EnumerateArguments().ToArray();

    public static ICorDebugValue[] GetLocalVariables(this ICorDebugILFrame instance) => instance.EnumerateLocalVariables().ToArray();

    public static (int pnOffset, CorDebugMappingResult pMappingResult) GetIP(this ICorDebugILFrame instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetIP(out var pnOffset, out var pMappingResult));
        return (pnOffset: checked((int)pnOffset), pMappingResult: pMappingResult);
    }

    public static void SetIP(this ICorDebugILFrame instance, int nOffset) {
        Marshal.ThrowExceptionForHR(instance.TrySetIP(checked((uint)nOffset)));
    }

    private static IEnumerable<ICorDebugValue> EnumerateArgumentsCore(ICorDebugValueEnum enumerator) {
        while (true) {
            var array = new ICorDebugValue[1];
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

    private static IEnumerable<ICorDebugValue> EnumerateLocalVariablesCore(ICorDebugValueEnum enumerator) {
        while (true) {
            var array = new ICorDebugValue[1];
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