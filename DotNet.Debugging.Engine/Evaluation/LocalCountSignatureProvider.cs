using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DotNet.Debugging.Engine.Evaluation;

// Decodes a local signature only to count the locals it declares
internal sealed class LocalCountSignatureProvider : ISignatureTypeProvider<object, object?> {
    private static readonly object type = new object();

    public static LocalCountSignatureProvider Instance { get; } = new LocalCountSignatureProvider();

    public object GetArrayType(object elementType, ArrayShape shape) => type;
    public object GetByReferenceType(object elementType) => type;
    public object GetFunctionPointerType(MethodSignature<object> signature) => type;
    public object GetGenericInstantiation(object genericType, ImmutableArray<object> typeArguments) => type;
    public object GetGenericMethodParameter(object? genericContext, int index) => type;
    public object GetGenericTypeParameter(object? genericContext, int index) => type;
    public object GetModifiedType(object modifier, object unmodifiedType, bool isRequired) => type;
    public object GetPinnedType(object elementType) => type;
    public object GetPointerType(object elementType) => type;
    public object GetPrimitiveType(PrimitiveTypeCode typeCode) => type;
    public object GetSZArrayType(object elementType) => type;
    public object GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => type;
    public object GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => type;
    public object GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => type;
}
