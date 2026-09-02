using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

// The names the debugging API hands out are asked for twice: a call without a buffer reports the length
// (terminator included), the second one fills a buffer of that size
internal static class NativeStrings {
    public delegate int ReadFunc(char[] buffer, out uint length);

    public static string Read(uint length, ReadFunc read) {
        if (length == 0)
            return string.Empty;
        var buffer = new char[length];
        Marshal.ThrowExceptionForHR(read(buffer, out var copied));
        var count = Math.Min(buffer.Length, checked((int)copied));
        if (count > 0 && buffer[count - 1] == '\0')
            count--;
        return new string(buffer, 0, count);
    }
}
