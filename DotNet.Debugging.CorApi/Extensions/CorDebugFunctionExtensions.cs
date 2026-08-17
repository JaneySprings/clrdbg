using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugFunctionExtensions {
    public static ICorDebugClass GetClass(this ICorDebugFunction instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetClass(out var ppClass));
        return ppClass;
    }

    public static ICorDebugCode GetILCode(this ICorDebugFunction instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetILCode(out var ppCode));
        return ppCode;
    }

    public static SignatureToken GetLocalVarSigToken(this ICorDebugFunction instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetLocalVarSigToken(out var pmdSig));
        return pmdSig;
    }

    public static ICorDebugModule GetModule(this ICorDebugFunction instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetModule(out var ppModule));
        return ppModule;
    }

    public static MethodDefToken GetToken(this ICorDebugFunction instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetToken(out var pMethodDef));
        return pMethodDef;
    }
}