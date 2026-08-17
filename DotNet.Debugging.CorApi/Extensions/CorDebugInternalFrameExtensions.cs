using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugInternalFrameExtensions {
    public static CorDebugInternalFrameType GetFrameType(this ICorDebugInternalFrame instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetFrameType(out var pType));
        return pType;
    }
}