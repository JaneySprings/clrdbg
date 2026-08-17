using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugAppDomainExtensions {
    public static IEnumerable<ICorDebugStepper> EnumerateSteppers(this ICorDebugAppDomain instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateSteppers(out var ppSteppers));
        return EnumerateSteppersCore(ppSteppers);
    }

    private static IEnumerable<ICorDebugStepper> EnumerateSteppersCore(ICorDebugStepperEnum enumerator) {
        while (true) {
            var array = new ICorDebugStepper[1];
            var errorCode = enumerator.TryNext(1u, array, out var pceltFetched);
            if (pceltFetched == 0) {
                yield break;
            }
            Marshal.ThrowExceptionForHR(errorCode);
            if (pceltFetched != 1) {
                break;
            }
            yield return array[0];
        }
        throw new InvalidOperationException("Native debugger enumerator returned an invalid item count.");
    }
}