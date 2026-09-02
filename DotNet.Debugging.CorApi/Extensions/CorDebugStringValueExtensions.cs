using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugStringValueExtensions {
    // The runtime copies at most the buffer's size and reports the string's length without a terminator, so a
    // buffer of exactly that length receives the whole string - including a '\0' the string itself ends with
    public static string GetString(this ICorDebugStringValue instance) {
        var length = instance.GetLength();
        if (length == 0)
            return string.Empty;
        var buffer = new char[length];
        Marshal.ThrowExceptionForHR(instance.TryGetString((uint)length, out var copied, buffer));
        return new string(buffer, 0, Math.Min(length, checked((int)copied)));
    }

    public static int GetLength(this ICorDebugStringValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetLength(out var pcchString));
        return checked((int)pcchString);
    }
}
