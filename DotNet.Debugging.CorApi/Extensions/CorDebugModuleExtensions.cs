using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugModuleExtensions {
    public static string GetName(this ICorDebugModule instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetName(0u, out var pcchName, null));
        if (pcchName == 0) {
            return string.Empty;
        }
        for (var i = 0; i < 3; i++) {
            var num = pcchName;
            char[] array;
            int num2;
            checked {
                array = new char[(int)num];
                Marshal.ThrowExceptionForHR(instance.TryGetName(num, out var pcchName2, array));
                if (pcchName2 > num) {
                    pcchName = pcchName2;
                    continue;
                }
                num2 = (int)pcchName2;
            }
            if (num2 > 0 && array[num2 - 1] == '\0') {
                num2--;
            }
            return new string(array, 0, num2);
        }
        throw new InvalidOperationException("Native buffer size did not stabilize.");
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

    public static object? GetMetaDataInterface(this ICorDebugModule instance, ref Guid riid) {
        Marshal.ThrowExceptionForHR(instance.TryGetMetaDataInterface(ref riid, out var ppObj));
        return ppObj;
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