using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugTypeExtensions {
    public static ICorDebugType[] GetTypeParameters(this ICorDebugType instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateTypeParameters(out var types));
        return types.ToArray<ICorDebugType>(types.TryNext);
    }

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
}
