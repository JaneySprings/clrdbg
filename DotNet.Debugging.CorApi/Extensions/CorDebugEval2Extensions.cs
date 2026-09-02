using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugEval2Extensions {
    public static void CallParameterizedFunction(this ICorDebugEval2 instance, ICorDebugFunction pFunction, ICorDebugType[]? ppTypeArgs, ICorDebugValue[] ppArgs) {
        Marshal.ThrowExceptionForHR(instance.TryCallParameterizedFunction(pFunction, GetCount(ppTypeArgs), ppTypeArgs, (uint)ppArgs.Length, ppArgs));
    }

    public static void NewParameterizedArray(this ICorDebugEval2 instance, ICorDebugType pElementType, uint[] dims, uint[] lowBounds) {
        if (dims.Length != lowBounds.Length)
            throw new ArgumentException("The dimensions and the lower bounds must have one entry per rank.", nameof(lowBounds));
        Marshal.ThrowExceptionForHR(instance.TryNewParameterizedArray(pElementType, (uint)dims.Length, dims, lowBounds));
    }

    public static void NewParameterizedObject(this ICorDebugEval2 instance, ICorDebugFunction pConstructor, ICorDebugType[]? ppTypeArgs, ICorDebugValue[] ppArgs) {
        Marshal.ThrowExceptionForHR(instance.TryNewParameterizedObject(pConstructor, GetCount(ppTypeArgs), ppTypeArgs, (uint)ppArgs.Length, ppArgs));
    }

    public static void NewParameterizedObjectNoConstructor(this ICorDebugEval2 instance, ICorDebugClass pClass, ICorDebugType[]? ppTypeArgs) {
        Marshal.ThrowExceptionForHR(instance.TryNewParameterizedObjectNoConstructor(pClass, GetCount(ppTypeArgs), ppTypeArgs));
    }

    private static uint GetCount(ICorDebugType[]? typeArguments) {
        return typeArguments == null ? 0u : (uint)typeArguments.Length;
    }
}
