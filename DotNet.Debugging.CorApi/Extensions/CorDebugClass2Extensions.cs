using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugClass2Extensions {
    public static ICorDebugType GetParameterizedType(this ICorDebugClass2 instance, CorElementType elementType, int nTypeArgs, ICorDebugType[] ppTypeArgs) {
        Marshal.ThrowExceptionForHR(instance.TryGetParameterizedType(elementType, checked((uint)nTypeArgs), ppTypeArgs, out var ppType));
        return ppType;
    }
}