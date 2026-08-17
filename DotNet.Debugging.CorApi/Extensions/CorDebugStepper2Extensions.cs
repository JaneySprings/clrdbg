using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugStepper2Extensions {
    public static void SetJMC(this ICorDebugStepper2 instance, bool fIsJMCStepper) {
        Marshal.ThrowExceptionForHR(instance.TrySetJMC(fIsJMCStepper));
    }
}