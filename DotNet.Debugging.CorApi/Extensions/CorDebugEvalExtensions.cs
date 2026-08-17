using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugEvalExtensions {
    public static ICorDebugValue CreateValue(this ICorDebugEval instance, CorElementType elementType, ICorDebugClass? pElementClass) {
        Marshal.ThrowExceptionForHR(instance.TryCreateValue(elementType, pElementClass, out var ppValue));
        return ppValue;
    }

    public static ICorDebugValue GetResult(this ICorDebugEval instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetResult(out var ppResult));
        return ppResult;
    }

    public static ICorDebugThread GetThread(this ICorDebugEval instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetThread(out var ppThread));
        return ppThread;
    }

    public static void NewString(this ICorDebugEval instance, string @string) {
        Marshal.ThrowExceptionForHR(instance.TryNewString(@string));
    }
}