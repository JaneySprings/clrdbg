using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi.Extensions;

public static class CorDebugObjectValueExtensions {
    public static ICorDebugClass GetClass(this ICorDebugObjectValue instance) {
        Marshal.ThrowExceptionForHR(instance.TryGetClass(out var ppClass));
        return ppClass;
    }

    public static ICorDebugValue GetFieldValue(this ICorDebugObjectValue instance, ICorDebugClass pClass, FieldDefToken fieldDef) {
        Marshal.ThrowExceptionForHR(instance.TryGetFieldValue(pClass, fieldDef, out var ppValue));
        return ppValue;
    }
}