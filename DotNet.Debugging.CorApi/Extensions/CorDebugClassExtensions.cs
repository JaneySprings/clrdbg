using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugClassExtensions {
    public static ICorDebugModule GetModule(this ICorDebugClass instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetModule(out var pModule));
        return pModule;
    }

    public static TypeDefToken GetToken(this ICorDebugClass instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetToken(out var pTypeDef));
        return pTypeDef;
    }
}