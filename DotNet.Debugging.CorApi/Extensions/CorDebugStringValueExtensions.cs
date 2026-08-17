using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugStringValueExtensions {
    public static string GetString(this ICorDebugStringValue instance) {
        var num = instance.GetLength();
        if (num == 0) {
            return string.Empty;
        }
        var array = new char[num];
        int num2;
        checked {
            Marshal.ThrowExceptionForHR(instance.TryGetString((uint)num, out var pcchString, array));
            if (pcchString > num) {
                throw new InvalidOperationException("Native buffer size exceeded the reported capacity.");
            }
            num2 = (int)pcchString;
        }
        if (num2 > 0 && array[num2 - 1] == '\0') {
            num2--;
        }
        return new string(array, 0, num2);
    }

    public static int GetLength(this ICorDebugStringValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetLength(out var pcchString));
        return checked((int)pcchString);
    }
}