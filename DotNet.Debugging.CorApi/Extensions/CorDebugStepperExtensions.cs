using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugStepperExtensions {
    public static bool IsActive(this ICorDebugStepper instance) {
        Marshal.ThrowExceptionForHR(instance.TryIsActive(out var pbActive));
        return pbActive;
    }

    public static void Deactivate(this ICorDebugStepper instance) {
        Marshal.ThrowExceptionForHR(instance.TryDeactivate());
    }

    public static void SetInterceptMask(this ICorDebugStepper instance, CorDebugIntercept mask) {
        Marshal.ThrowExceptionForHR(instance.TrySetInterceptMask(mask));
    }

    public static void SetUnmappedStopMask(this ICorDebugStepper instance, CorDebugUnmappedStop mask) {
        Marshal.ThrowExceptionForHR(instance.TrySetUnmappedStopMask(mask));
    }

    public static void Step(this ICorDebugStepper instance, bool bStepIn) {
        Marshal.ThrowExceptionForHR(instance.TryStep(bStepIn));
    }

    public static void StepOut(this ICorDebugStepper instance) {
        Marshal.ThrowExceptionForHR(instance.TryStepOut());
    }

    public static void StepRange(this ICorDebugStepper instance, bool bStepIn, CorDebugStepRange[] ranges, int cRangeCount) {
        Marshal.ThrowExceptionForHR(instance.TryStepRange(bStepIn, ranges, checked((uint)cRangeCount)));
    }
}