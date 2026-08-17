using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugTypeExtensions {
    public static IEnumerable<ICorDebugType> EnumerateTypeParameters(this ICorDebugType instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateTypeParameters(out var ppTyParEnum));
        return EnumerateTypeParametersCore(ppTyParEnum);
    }

    public static ICorDebugType[] GetTypeParameters(this ICorDebugType instance) => instance.EnumerateTypeParameters().ToArray();

    public static ICorDebugType GetBase(this ICorDebugType instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetBase(out var pBase));
        return pBase;
    }

    public static ICorDebugClass GetClass(this ICorDebugType instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetClass(out var ppClass));
        return ppClass;
    }

    public static ICorDebugType GetFirstTypeParameter(this ICorDebugType instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetFirstTypeParameter(out var value));
        return value;
    }

    public static int GetRank(this ICorDebugType instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetRank(out var pnRank));
        return checked((int)pnRank);
    }

    public static ICorDebugValue GetStaticFieldValue(this ICorDebugType instance, FieldDefToken fieldDef, ICorDebugFrame pFrame) {
        Marshal.ThrowExceptionForHR(instance.TryGetStaticFieldValue(fieldDef, pFrame, out var ppValue));
        return ppValue;
    }

    public static CorElementType GetElementType(this ICorDebugType instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetType(out var ty));
        return ty;
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