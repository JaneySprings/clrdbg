namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugEvalEval2Extensions {
    public static ICorDebugEval2 GetEval2(this ICorDebugEval instance) => (instance as ICorDebugEval2) ?? throw new NotSupportedException("ICorDebugEval does not support ICorDebugEval2.");

    public static void CallParameterizedFunction(this ICorDebugEval instance, ICorDebugFunction pFunction, ICorDebugType[]? ppTypeArgs, ICorDebugValue[] ppArgs) {
        instance.GetEval2().CallParameterizedFunction(pFunction, ppTypeArgs, ppArgs);
    }

    public static void NewParameterizedArray(this ICorDebugEval instance, ICorDebugType pElementType, uint[] dims, uint[] lowBounds) {
        instance.GetEval2().NewParameterizedArray(pElementType, dims, lowBounds);
    }

    public static void NewParameterizedObject(this ICorDebugEval instance, ICorDebugFunction pConstructor, ICorDebugType[]? ppTypeArgs, ICorDebugValue[] ppArgs) {
        instance.GetEval2().NewParameterizedObject(pConstructor, ppTypeArgs, ppArgs);
    }

    public static void NewParameterizedObjectNoConstructor(this ICorDebugEval instance, ICorDebugClass pClass, ICorDebugType[]? ppTypeArgs) {
        instance.GetEval2().NewParameterizedObjectNoConstructor(pClass, ppTypeArgs);
    }
}
