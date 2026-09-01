using System.Runtime.InteropServices;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Metadata;

namespace DotNet.Debugging.Engine.Extensions;

internal static class MetadataImportExtensions {
    public static bool IsStatic(this FieldDefToken fieldDef, IMetaDataImport metadataImport) {
        return metadataImport.GetFieldProps(fieldDef).pdwAttr.IsFdStatic();
    }
    public static bool IsLiteral(this FieldDefToken fieldDef, IMetaDataImport metadataImport) {
        return metadataImport.GetFieldProps(fieldDef).pdwAttr.IsFdLiteral();
    }
    public static bool IsStatic(this PropertyToken property, IMetaDataImport metadataImport) {
        var getter = metadataImport.GetPropertyProps(property).pmdGetter;
        return !getter.IsNil && metadataImport.GetMethodProps(getter).pdwAttr.IsMdStatic();
    }
    // An indexer getter requires arguments, so the property cannot be evaluated as a member
    public static bool IsIndexer(this PropertyToken property, IMetaDataImport metadataImport) {
        var getter = metadataImport.GetPropertyProps(property).pmdGetter;
        return !getter.IsNil && Marshal.ReadByte(metadataImport.GetMethodProps(getter).ppvSigBlob, 1) != 0;
    }

    // A method the stepper must not stop in: [DebuggerStepThrough] or [DebuggerHidden] on the method or its
    // type, plus [DebuggerNonUserCode] while Just My Code is on
    public static bool IsNonUserMethod(this IMetaDataImport metadataImport, MethodDefToken methodToken, bool justMyCode) {
        var attributeNames = justMyCode ? AttributeNames.JustMyCodeNonUserMethodAttributes : AttributeNames.NonUserMethodAttributes;
        if (metadataImport.HasAnyAttribute(methodToken, attributeNames))
            return true;
        return metadataImport.HasAnyAttribute(metadataImport.GetMethodProps(methodToken).pClass, attributeNames);
    }
    // A property accessor or an operator method, what 'step over properties and operators' filters out
    public static bool IsPropertyOrOperator(this IMetaDataImport metadataImport, MethodDefToken methodToken) {
        var methodProps = metadataImport.GetMethodProps(methodToken);
        if (!methodProps.pdwAttr.IsMdSpecialName())
            return false;
        return methodProps.szMethod.StartsWith("get_") || methodProps.szMethod.StartsWith("set_") || methodProps.szMethod.StartsWith("op_");
    }

    public static bool HasAttribute(this IMetaDataImport metadataImport, MetadataToken token, string attributeName) {
        return metadataImport.TryGetCustomAttributeByName(token, attributeName, out _, out _) == Cor.S_OK;
    }
    public static bool HasAnyAttribute(this IMetaDataImport metadataImport, MetadataToken token, string[] attributeNames) {
        foreach (var attributeName in attributeNames) {
            if (metadataImport.HasAttribute(token, attributeName))
                return true;
        }
        return false;
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
}
