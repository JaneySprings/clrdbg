namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugILFrameILFrame2Extensions {
    public static ICorDebugILFrame2 GetILFrame2(this ICorDebugILFrame instance) => (instance as ICorDebugILFrame2) ?? throw new NotSupportedException("ICorDebugILFrame does not support ICorDebugILFrame2.");

    public static ICorDebugType[] GetTypeParameters(this ICorDebugILFrame instance) => instance.GetILFrame2().GetTypeParameters();
}
