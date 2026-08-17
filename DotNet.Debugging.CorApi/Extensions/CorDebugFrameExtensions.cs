using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugFrameExtensions {
    public static ICorDebugStepper CreateStepper(this ICorDebugFrame instance) {
        Marshal.ThrowExceptionForHR(instance.TryCreateStepper(out var ppStepper));
        return ppStepper;
    }

    public static ICorDebugFrame GetCaller(this ICorDebugFrame instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetCaller(out var ppFrame));
        return ppFrame;
    }

    public static ICorDebugChain GetChain(this ICorDebugFrame instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetChain(out var ppChain));
        return ppChain;
    }

    public static ICorDebugFunction GetFunction(this ICorDebugFrame instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetFunction(out var ppFunction));
        return ppFunction;
    }
}