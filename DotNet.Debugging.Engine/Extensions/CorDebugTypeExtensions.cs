using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;

namespace DotNet.Debugging.Engine.Extensions;

internal static class CorDebugTypeExtensions {
    // The base of 'System.Object' is a null type locally, but the remote (mobile) transport hands back a type
    // without an element type instead, whose class cannot be obtained
    public static ICorDebugType? GetBaseType(this ICorDebugType type) {
        var baseType = type.GetBase();
        if (baseType == null || baseType.GetElementType() == CorElementType.END)
            return null;
        return baseType;
    }
}
