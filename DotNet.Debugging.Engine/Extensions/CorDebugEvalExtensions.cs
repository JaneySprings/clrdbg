using System.Runtime.InteropServices;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;

namespace DotNet.Debugging.Engine.Extensions;

internal static class CorDebugEvalExtensions {
    public static ICorDebugValue CreateBooleanValue(this ICorDebugEval eval, bool value) {
        var corValue = eval.CreateValue(CorElementType.BOOLEAN, null);
        if (value && corValue is ICorDebugGenericValue genericValue)
            genericValue.SetValueFromBytes([1]);
        return corValue;
    }
    // The result of a completed call, null for a method without one
    public static ICorDebugValue? GetCallResult(this ICorDebugEval eval) {
        var result = eval.TryGetResult(out var value);
        if (result != Cor.CORDBG_S_FUNC_EVAL_HAS_NO_RESULT && value == null)
            Marshal.ThrowExceptionForHR(result);
        return value;
    }
}
