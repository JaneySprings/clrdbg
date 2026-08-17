using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugValueValue2Extensions {
    public static ICorDebugValue2 GetValue2(this ICorDebugValue instance) => (instance as ICorDebugValue2) ?? throw new NotSupportedException("ICorDebugValue does not support ICorDebugValue2.");

    public static ICorDebugType GetExactType(this ICorDebugValue instance) {
        Marshal.ThrowExceptionForHR(instance.GetValue2().TryGetExactType(out var ppType));
        return ppType;
    }
}