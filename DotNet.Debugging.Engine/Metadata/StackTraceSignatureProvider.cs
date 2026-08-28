using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DotNet.Debugging.Engine.Metadata;

// Formats signature types the way the runtime's Exception.StackTrace shows parameters: the reflection
// names of the primitives ('Int32', 'String') and short names without a namespace for everything else
internal sealed class StackTraceSignatureProvider : ISignatureTypeProvider<string, object?> {
    public static StackTraceSignatureProvider Instance { get; } = new StackTraceSignatureProvider();

    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "IntPtr";
    // A constructed generic keeps the reflection name of its definition ('List`1'), without the arguments
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType;
    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeDefinition(handle).Name);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeReference(handle).Name);
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) {
        return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
    // The enum member names ('Int32', 'String', 'Void') are exactly the reflection names
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
}
