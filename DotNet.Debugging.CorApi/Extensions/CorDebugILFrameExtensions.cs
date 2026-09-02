using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugILFrameExtensions {
    // One value per slot, null for a slot the runtime cannot read at this instruction (a variable optimized away)
    public static ICorDebugValue?[] GetArguments(this ICorDebugILFrame instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateArguments(out var values));
        return GetValues(values);
    }

    public static ICorDebugValue?[] GetLocalVariables(this ICorDebugILFrame instance) {
        Marshal.ThrowExceptionForHR(instance.TryEnumerateLocalVariables(out var values));
        return GetValues(values);
    }

    public static (int pnOffset, CorDebugMappingResult pMappingResult) GetIP(this ICorDebugILFrame instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetIP(out var pnOffset, out var pMappingResult));
        return (pnOffset: checked((int)pnOffset), pMappingResult: pMappingResult);
    }

    public static void SetIP(this ICorDebugILFrame instance, int nOffset) {
        Marshal.ThrowExceptionForHR(instance.TrySetIP(checked((uint)nOffset)));
    }

    // 'Next' stops at a slot it cannot read, reports the ones before it and moves past it, so the fetch
    // resumes behind the unreadable slot until every slot is accounted for
    private static ICorDebugValue?[] GetValues(ICorDebugValueEnum values) {
        Marshal.ThrowExceptionForHR(values.TryGetCount(out var count));
        var result = new ICorDebugValue?[count];
        var position = 0;
        while (position < result.Length) {
            var remaining = new ICorDebugValue[result.Length - position];
            var hr = values.TryNext((uint)remaining.Length, remaining, out var fetched);
            Array.Copy(remaining, 0, result, position, checked((int)fetched));
            position += (int)fetched;
            if (hr >= 0)
                break;
            if (hr != Cor.CORDBG_E_IL_VAR_NOT_AVAILABLE)
                Marshal.ThrowExceptionForHR(hr);
            position++;
        }
        return result;
    }
}
