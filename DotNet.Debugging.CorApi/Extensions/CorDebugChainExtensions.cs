using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugChainExtensions {
    public static ICorDebugFrame[] GetFrames(this ICorDebugChain instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateFrames(out var frames));
        return frames.ToArray<ICorDebugFrame>(frames.TryNext);
    }

    public static ICorDebugThread GetThread(this ICorDebugChain instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetThread(out var ppThread));
        return ppThread;
    }

    public static bool IsManaged(this ICorDebugChain instance) {
        Marshal.ThrowExceptionForHR(instance.TryIsManaged(out var pManaged));
        return pManaged;
    }
}
