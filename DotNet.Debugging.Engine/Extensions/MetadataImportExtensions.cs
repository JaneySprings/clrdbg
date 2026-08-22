using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;

namespace DotNet.Debugging.Engine.Extensions;

internal static class MetadataImportExtensions {
    public static bool IsStatic(this FieldDefToken fieldDef, IMetaDataImport metadataImport) {
        return metadataImport.GetFieldProps(fieldDef).pdwAttr.IsFdStatic();
    }
    public static bool IsLiteral(this FieldDefToken fieldDef, IMetaDataImport metadataImport) {
        return metadataImport.GetFieldProps(fieldDef).pdwAttr.IsFdLiteral();
    }
    public static bool IsPublic(this FieldDefToken fieldDef, IMetaDataImport metadataImport) {
        return metadataImport.GetFieldProps(fieldDef).pdwAttr.IsFdPublic();
    }
    public static bool IsStatic(this PropertyToken property, IMetaDataImport metadataImport) {
        var getter = metadataImport.GetPropertyProps(property).pmdGetter;
        return !getter.IsNil && metadataImport.GetMethodProps(getter).pdwAttr.IsMdStatic();
    }
    public static bool IsPublic(this PropertyToken property, IMetaDataImport metadataImport) {
        var getter = metadataImport.GetPropertyProps(property).pmdGetter;
        return !getter.IsNil && metadataImport.GetMethodProps(getter).pdwAttr.IsMdPublic();
    }
    public static bool HasGetter(this PropertyToken property, IMetaDataImport metadataImport) {
        return !metadataImport.GetPropertyProps(property).pmdGetter.IsNil;
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
