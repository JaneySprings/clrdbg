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
    public static bool IsEnumType(this ICorDebugType type) {
        var baseType = type.GetBaseType();
        if (baseType == null)
            return false;
        var corClass = baseType.GetClass();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        return metadataImport.GetTypeDefProps(corClass.GetToken()).szTypeDef == "System.Enum";
    }
}
