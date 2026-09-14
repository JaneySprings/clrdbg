using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Metadata;
using DotNet.Debugging.Engine.Variables;

namespace DotNet.Debugging.Engine.Extensions;

internal static class MetadataImportExtensions {
    public static bool IsStatic(this FieldDefToken fieldDef, IMetaDataImport metadataImport) {
        return metadataImport.GetFieldProps(fieldDef).pdwAttr.IsFdStatic();
    }
    public static bool IsLiteral(this FieldDefToken fieldDef, IMetaDataImport metadataImport) {
        return metadataImport.GetFieldProps(fieldDef).pdwAttr.IsFdLiteral();
    }

    // A method the stepper must not stop in: [DebuggerStepThrough] or [DebuggerHidden] on the method or its
    // type, plus [DebuggerNonUserCode] while Just My Code is on
    public static bool IsNonUserMethod(this IMetaDataImport metadataImport, MethodDefToken methodToken, bool justMyCode) {
        var attributeNames = justMyCode ? AttributeNames.JustMyCodeNonUserMethodAttributes : AttributeNames.NonUserMethodAttributes;
        return attributeNames.Any(it => metadataImport.HasMethodOrTypeAttribute(methodToken, it));
    }
    // Whether the method carries the attribute, directly or through its declaring type
    public static bool HasMethodOrTypeAttribute(this IMetaDataImport metadataImport, MethodDefToken methodToken, string attributeName) {
        if (metadataImport.HasAttribute(methodToken, attributeName))
            return true;
        return metadataImport.HasAttribute(metadataImport.GetMethodProps(methodToken).pClass, attributeName);
    }
    // A property accessor or an operator method, what 'step over properties and operators' filters out
    public static bool IsPropertyOrOperator(this IMetaDataImport metadataImport, MethodDefToken methodToken) {
        var methodProps = metadataImport.GetMethodProps(methodToken);
        if (!methodProps.pdwAttr.IsMdSpecialName())
            return false;
        // An explicit interface implementation carries the interface's name in front ('Ns.IShape.get_Area')
        var name = methodProps.szMethod.Substring(methodProps.szMethod.LastIndexOf('.') + 1);
        return name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal) || name.StartsWith("op_", StringComparison.Ordinal);
    }
    // Whether the type extends System.ValueType or System.Enum
    public static bool IsValueType(this IMetaDataImport metadataImport, TypeDefToken typeDef) {
        var extends = metadataImport.GetTypeDefProps(typeDef).ptkExtends;
        if (extends.IsNil)
            return false;
        string baseTypeName;
        if (extends.Type == CorTokenType.mdtTypeDef)
            baseTypeName = metadataImport.GetTypeDefProps(new TypeDefToken(extends.Value)).szTypeDef;
        else if (extends.Type == CorTokenType.mdtTypeRef)
            baseTypeName = metadataImport.GetTypeRefProps(new TypeRefToken(extends.Value)).szName;
        else
            return false;
        return baseTypeName == "System.ValueType" || baseTypeName == "System.Enum";
    }

    // The name of a method's parameter at its 1-based position, null when the method has no Param row for it (a
    // Reflection.Emit method defined without parameter names) or the row carries no name
    public static string? FindParameterName(this IMetaDataImport metadataImport, MethodDefToken methodToken, int sequence) {
        if (metadataImport.TryGetParamForMethodIndex(methodToken, checked((uint)sequence), out var paramDef) != Cor.S_OK)
            return null;
        var name = metadataImport.GetParamProps(paramDef).szName;
        return string.IsNullOrEmpty(name) ? null : name;
    }

    public static bool HasAttribute(this IMetaDataImport metadataImport, MetadataToken token, string attributeName) {
        return metadataImport.TryGetCustomAttributeByName(token, attributeName, out _, out _) == Cor.S_OK;
    }
    // Whether the method, or the type declaring it, is marked [StackTraceHidden]
    public static bool IsStackTraceHidden(this IMetaDataImport metadataImport, MethodDefToken methodToken) {
        return metadataImport.HasAttribute(methodToken, AttributeNames.StackTraceHidden)
            || metadataImport.HasAttribute(metadataImport.GetMethodProps(methodToken).pClass, AttributeNames.StackTraceHidden);
    }
    public static DebuggerBrowsableState? GetDebuggerBrowsableState(this IMetaDataImport metadataImport, MetadataToken token) {
        if (metadataImport.TryGetCustomAttributeByName(token, AttributeNames.DebuggerBrowsable, out var data, out var size) != Cor.S_OK)
            return null;
        return (DebuggerBrowsableState)CustomAttributeReader.ReadInt32Argument(data, size);
    }

    public static TypeDefToken? FindTypeDef(this IMetaDataImport metadataImport, string typeName, MetadataToken enclosingClass) {
        if (metadataImport.TryFindTypeDefByName(typeName, enclosingClass, out var typeDef) != Cor.S_OK)
            return null;
        return typeDef;
    }
    // Resolves 'Outer+Nested' names, the form used by custom attributes that reference a type
    public static TypeDefToken? FindNestedTypeDef(this IMetaDataImport metadataImport, string typeName) {
        TypeDefToken? enclosingClass = null;
        foreach (var name in typeName.Split('+')) {
            var typeDef = metadataImport.FindTypeDef(name, enclosingClass ?? MetadataToken.Nil);
            if (typeDef == null)
                return null;
            enclosingClass = typeDef;
        }
        return enclosingClass;
    }
    public static PropertyToken? FindProperty(this IMetaDataImport metadataImport, TypeDefToken typeDef, string propertyName) {
        foreach (var property in metadataImport.EnumProperties(typeDef)) {
            if (!property.IsNil && metadataImport.GetPropertyProps(property).szProperty == propertyName)
                return property;
        }
        return null;
    }
    // The metadata name of the type a token refers to: a definition's or a reference's own, the definition's for a
    // generic instantiation ('System.Collections.Generic.IEnumerable`1'), null for another token kind
    public static string? GetTypeName(this IMetaDataImport metadataImport, MetadataToken token) {
        switch (token.Type) {
            case CorTokenType.mdtTypeDef:
                return metadataImport.GetTypeDefProps((int)token).szTypeDef;
            case CorTokenType.mdtTypeRef:
                return metadataImport.GetTypeRefProps((int)token).szName;
            case CorTokenType.mdtTypeSpec:
                return GetTypeSpecName(metadataImport, (int)token);
            default:
                return null;
        }
    }
    // A generic instantiation is a type spec: GENERICINST, CLASS or VALUETYPE, then the coded type token
    private static unsafe string? GetTypeSpecName(IMetaDataImport metadataImport, TypeSpecToken token) {
        var (signature, size) = metadataImport.GetTypeSpecFromToken(token);
        var reader = new BlobReader((byte*)signature, size);
        if (reader.Length < 3 || reader.ReadByte() != (byte)CorElementType.GENERICINST)
            return null;
        reader.ReadByte(); // CLASS or VALUETYPE
        var handle = reader.ReadTypeHandle();
        if (handle.Kind != HandleKind.TypeDefinition && handle.Kind != HandleKind.TypeReference)
            return null;
        return metadataImport.GetTypeName(MetadataTokens.GetToken(handle));
    }
}
