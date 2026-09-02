using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugEnumExtensions {
    public delegate int NextFunc<T>(uint celt, T[] items, out uint pceltFetched);

    // Every item of the enumeration in one round trip: the count first, then a single 'Next' for all of them.
    // The result is checked before the fetched count, which a failed 'Next' leaves at zero
    public static T[] ToArray<T>(this ICorDebugEnum enumerator, NextFunc<T> next) {
        Marshal.ThrowExceptionForHR(enumerator.TryGetCount(out var count));
        if (count == 0)
            return [];
        var items = new T[count];
        Marshal.ThrowExceptionForHR(next(count, items, out var fetched));
        if (fetched < count)
            Array.Resize(ref items, checked((int)fetched));
        return items;
    }
}
