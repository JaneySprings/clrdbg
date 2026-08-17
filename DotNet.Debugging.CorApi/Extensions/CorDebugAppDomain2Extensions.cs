using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugAppDomain2Extensions {
    public static ICorDebugType GetArrayOrPointerType(this ICorDebugAppDomain2 instance, CorElementType elementType, int nRank, ICorDebugType pTypeArg) {
        Marshal.ThrowExceptionForHR(instance.TryGetArrayOrPointerType(elementType, checked((uint)nRank), pTypeArg, out var ppType));
        return ppType;
    }
}