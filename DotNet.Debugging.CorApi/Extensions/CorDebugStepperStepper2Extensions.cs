namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugStepperStepper2Extensions {
    public static ICorDebugStepper2 GetStepper2(this ICorDebugStepper instance) => (instance as ICorDebugStepper2) ?? throw new NotSupportedException("ICorDebugStepper does not support ICorDebugStepper2.");

    public static void SetJMC(this ICorDebugStepper instance, bool fIsJMCStepper) {
        instance.GetStepper2().SetJMC(fIsJMCStepper);
    }
}