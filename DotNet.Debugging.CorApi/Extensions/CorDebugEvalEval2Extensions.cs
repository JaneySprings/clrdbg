namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugEvalEval2Extensions {
    public static ICorDebugEval2 GetEval2(this ICorDebugEval instance) => (instance as ICorDebugEval2) ?? throw new NotSupportedException("ICorDebugEval does not support ICorDebugEval2.");

    public static void CallParameterizedFunction(this ICorDebugEval instance, ICorDebugFunction pFunction, int nTypeArgs, ICorDebugType[]? ppTypeArgs, int nArgs, ICorDebugValue[] ppArgs) {
        instance.GetEval2().CallParameterizedFunction(pFunction, nTypeArgs, ppTypeArgs, nArgs, ppArgs);
    }

    public static void NewParameterizedArray(this ICorDebugEval instance, ICorDebugType pElementType, int rank, uint[] dims, uint[] lowBounds) {
        instance.GetEval2().NewParameterizedArray(pElementType, rank, dims, lowBounds);
    }

    public static void NewParameterizedObject(this ICorDebugEval instance, ICorDebugFunction pConstructor, int nTypeArgs, ICorDebugType[]? ppTypeArgs, int nArgs, ICorDebugValue[] ppArgs) {
        instance.GetEval2().NewParameterizedObject(pConstructor, nTypeArgs, ppTypeArgs, nArgs, ppArgs);
    }

    public static void NewParameterizedObjectNoConstructor(this ICorDebugEval instance, ICorDebugClass pClass, int nTypeArgs, ICorDebugType[]? ppTypeArgs) {
        instance.GetEval2().NewParameterizedObjectNoConstructor(pClass, nTypeArgs, ppTypeArgs);
    }
}