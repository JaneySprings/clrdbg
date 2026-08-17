using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugEval2Extensions {
    public static void CallParameterizedFunction(this ICorDebugEval2 instance, ICorDebugFunction pFunction, int nTypeArgs, ICorDebugType[]? ppTypeArgs, int nArgs, ICorDebugValue[] ppArgs) {
        Marshal.ThrowExceptionForHR(checked(instance.TryCallParameterizedFunction(pFunction, (uint)nTypeArgs, ppTypeArgs, (uint)nArgs, ppArgs)));
    }

    public static void NewParameterizedArray(this ICorDebugEval2 instance, ICorDebugType pElementType, int rank, uint[] dims, uint[] lowBounds) {
        Marshal.ThrowExceptionForHR(instance.TryNewParameterizedArray(pElementType, checked((uint)rank), dims, lowBounds));
    }

    public static void NewParameterizedObject(this ICorDebugEval2 instance, ICorDebugFunction pConstructor, int nTypeArgs, ICorDebugType[]? ppTypeArgs, int nArgs, ICorDebugValue[] ppArgs) {
        Marshal.ThrowExceptionForHR(checked(instance.TryNewParameterizedObject(pConstructor, (uint)nTypeArgs, ppTypeArgs, (uint)nArgs, ppArgs)));
    }

    public static void NewParameterizedObjectNoConstructor(this ICorDebugEval2 instance, ICorDebugClass pClass, int nTypeArgs, ICorDebugType[]? ppTypeArgs) {
        Marshal.ThrowExceptionForHR(instance.TryNewParameterizedObjectNoConstructor(pClass, checked((uint)nTypeArgs), ppTypeArgs));
    }
}