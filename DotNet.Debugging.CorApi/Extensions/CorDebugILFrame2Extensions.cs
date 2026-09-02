using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugILFrame2Extensions {
    public static ICorDebugType[] GetTypeParameters(this ICorDebugILFrame2 instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateTypeParameters(out var types));
        return types.ToArray<ICorDebugType>(types.TryNext);
    }
}
