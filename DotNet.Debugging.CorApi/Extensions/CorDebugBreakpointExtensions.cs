using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugBreakpointExtensions {
    public static void Activate(this ICorDebugBreakpoint instance, bool bActive) {
        Marshal.ThrowExceptionForHR(instance.TryActivate(bActive));
    }
}