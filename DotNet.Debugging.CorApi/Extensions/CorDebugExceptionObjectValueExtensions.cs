using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugExceptionObjectValueExtensions {
    public static CorDebugExceptionObjectStackFrame[] GetExceptionCallStack(this ICorDebugExceptionObjectValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateExceptionCallStack(out var frames));
        return frames.ToArray<CorDebugExceptionObjectStackFrame>(frames.TryNext);
    }
}
