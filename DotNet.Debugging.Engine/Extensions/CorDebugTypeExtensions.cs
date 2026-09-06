using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Evaluation;

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
    // The base of the receiver's type that is 'declaringType', carrying its instantiation. Null when the declaring
    // type is outside the class chain (an interface, an array's methods)
    public static ICorDebugType? FindDeclaringType(this ICorDebugType receiverType, ResolvedRuntimeType declaringType) {
        var declaringToken = (TypeDefToken)MetadataTokens.GetToken(declaringType.Handle);
        for (var type = (ICorDebugType?)receiverType; type != null; type = type.GetBaseType()) {
            if (type.GetElementType() is not (CorElementType.VALUETYPE or CorElementType.CLASS))
                return null;
            var corClass = type.GetClass();
            if (corClass.GetToken() == declaringToken && corClass.GetModule() == declaringType.Module.Module)
                return type;
        }
        return null;
    }

    // The element type of a primitive's class ('System.Int32' is I4), null for any other type
    public static CorElementType? GetPrimitiveElementType(this ICorDebugType type) {
        var corClass = type.GetClass();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        return metadataImport.GetTypeDefProps(corClass.GetToken()).szTypeDef switch {
            "System.Boolean" => CorElementType.BOOLEAN,
            "System.Char" => CorElementType.CHAR,
            "System.SByte" => CorElementType.I1,
            "System.Byte" => CorElementType.U1,
            "System.Int16" => CorElementType.I2,
            "System.UInt16" => CorElementType.U2,
            "System.Int32" => CorElementType.I4,
            "System.UInt32" => CorElementType.U4,
            "System.Int64" => CorElementType.I8,
            "System.UInt64" => CorElementType.U8,
            "System.Single" => CorElementType.R4,
            "System.Double" => CorElementType.R8,
            "System.IntPtr" => CorElementType.I,
            "System.UIntPtr" => CorElementType.U,
            _ => null
        };
    }
    public static bool IsEnumType(this ICorDebugType type) {
        var baseType = type.GetBaseType();
        if (baseType == null)
            return false;
        var corClass = baseType.GetClass();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        return metadataImport.GetTypeDefProps(corClass.GetToken()).szTypeDef == "System.Enum";
    }
    public static bool IsExceptionType(this ICorDebugType type) {
        var current = type;
        while (current != null) {
            var corClass = current.GetClass();
            var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
            if (metadataImport.GetTypeDefProps(corClass.GetToken()).szTypeDef == "System.Exception")
                return true;
            current = current.GetBaseType();
        }
        return false;
    }
    public static bool IsRootType(this ICorDebugType type) {
        var corClass = type.GetClass();
        var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
        return metadataImport.GetTypeDefProps(corClass.GetToken()).szTypeDef is "System.Object" or "System.ValueType" or "System.Enum";
    }
    // The C# compiler emits the transitive closure of implemented interfaces, so checking the base classes is enough
    public static bool IsEnumerableType(this ICorDebugType type) {
        for (var current = type; current != null && !current.IsRootType(); current = current.GetBaseType()) {
            var corClass = current.GetClass();
            var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
            foreach (var impl in metadataImport.EnumInterfaceImpls(corClass.GetToken())) {
                var interfaceName = metadataImport.GetTypeName(metadataImport.GetInterfaceImplProps(impl).ptkIface);
                if (interfaceName is "System.Collections.IEnumerable" or "System.Collections.Generic.IEnumerable`1")
                    return true;
            }
        }
        return false;
    }
    // Whether the type or any base type up to System.Object/System.ValueType declares a parameterless ToString override
    public static bool OverridesToString(this ICorDebugType type) {
        var current = type;
        while (current != null) {
            var corClass = current.GetClass();
            var metadataImport = corClass.GetModule().GetMetaDataInterface<IMetaDataImport>();
            var typeName = metadataImport.GetTypeDefProps(corClass.GetToken()).szTypeDef;
            if (typeName == "System.Object" || typeName == "System.ValueType")
                return false;

            foreach (var methodToken in metadataImport.EnumMethodsWithName(corClass.GetToken(), "ToString")) {
                var methodProps = metadataImport.GetMethodProps(methodToken);
                var attributes = methodProps.pdwAttr;
                var parameterCount = Marshal.ReadByte(methodProps.ppvSigBlob, 1);
                if (!attributes.IsMdStatic() && attributes.IsMdVirtual() && !attributes.IsMdNewSlot() && parameterCount == 0)
                    return true;
            }
            current = current.GetBaseType();
        }
        return false;
    }

    // The native API takes null rather than an empty array for a non-generic instantiation
    public static ICorDebugType[]? NullIfEmpty(this ICorDebugType[] typeArguments) {
        return typeArguments.Length == 0 ? null : typeArguments;
    }
}
