using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;

namespace DotNet.Debugging.Engine.Extensions;

internal static class CorDebugCodeExtensions {
    // The IL offset a native offset of the jitted code maps to. False for an offset outside the mapping and
    // for the prolog or epilog, whose special offsets (-2, -3) have no source mapped to them
    public static bool TryGetILOffset(this ICorDebugCode nativeCode, ulong nativeOffset, out int ilOffset) {
        ilOffset = 0;
        foreach (var entry in nativeCode.GetILToNativeMapping()) {
            if (nativeOffset < entry.nativeStartOffset || nativeOffset >= entry.nativeEndOffset)
                continue;
            ilOffset = (int)entry.ilOffset;
            return ilOffset >= 0;
        }
        return false;
    }
}
