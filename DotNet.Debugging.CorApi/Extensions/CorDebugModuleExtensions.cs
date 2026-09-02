using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugModuleExtensions {
    public static string GetName(this ICorDebugModule instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetName(0u, out var pcchName, null));
        return NativeStrings.Read(pcchName, (char[] buffer, out uint length) => instance.TryGetName((uint)buffer.Length, out length, buffer));
    }

    public static CordbAddress GetBaseAddress(this ICorDebugModule instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetBaseAddress(out var pAddress));
        return pAddress;
    }

    public static ICorDebugClass GetClassFromToken(this ICorDebugModule instance, TypeDefToken typeDef) {
        Marshal.ThrowExceptionForHR(instance.TryGetClassFromToken(typeDef, out var ppClass));
        return ppClass;
    }

    public static ICorDebugFunction GetFunctionFromToken(this ICorDebugModule instance, MethodDefToken methodDef) {
        Marshal.ThrowExceptionForHR(instance.TryGetFunctionFromToken(methodDef, out var ppFunction));
        return ppFunction;
    }

    public static int GetSize(this ICorDebugModule instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetSize(out var pcBytes));
        return checked((int)pcBytes);
    }

    public static ModuleToken GetToken(this ICorDebugModule instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetToken(out var pToken));
        return pToken;
    }

    public static bool IsDynamic(this ICorDebugModule instance) {
        Marshal.ThrowExceptionForHR(instance.TryIsDynamic(out var pDynamic));
        return pDynamic;
    }

    public static bool IsInMemory(this ICorDebugModule instance) {
        Marshal.ThrowExceptionForHR(instance.TryIsInMemory(out var pInMemory));
        return pInMemory;
    }
}
