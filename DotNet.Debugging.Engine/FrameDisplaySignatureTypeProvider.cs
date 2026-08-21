using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DotNet.Debugging.Engine;

/// <summary>
/// Formats signature types the way vsdbg shows them in stack frame names: C# keywords for primitives,
/// namespace-qualified names for everything else and generic instantiations as 'List&lt;int&gt;'
/// </summary>
internal sealed class FrameDisplaySignatureTypeProvider : ISignatureTypeProvider<string, object?> {
    public static FrameDisplaySignatureTypeProvider Instance { get; } = new();

    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";
    public string GetByReferenceType(string elementType) => "ref " + elementType;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => $"{StripArity(genericType)}<{string.Join(", ", typeArguments)}>";
    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => FunctionBreakpointSignatureTypeProvider.GetTypeName(reader, handle);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => FunctionBreakpointSignatureTypeProvider.GetTypeName(reader, handle);
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch {
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Byte => "byte",
        PrimitiveTypeCode.SByte => "sbyte",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.Int16 => "short",
        PrimitiveTypeCode.UInt16 => "ushort",
        PrimitiveTypeCode.Int32 => "int",
        PrimitiveTypeCode.UInt32 => "uint",
        PrimitiveTypeCode.Int64 => "long",
        PrimitiveTypeCode.UInt64 => "ulong",
        PrimitiveTypeCode.Single => "float",
        PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.IntPtr => "nint",
        PrimitiveTypeCode.UIntPtr => "nuint",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.TypedReference => "System.TypedReference",
        _ => typeCode.ToString()
    };

    // 'System.Collections.Generic.List`1' -> 'System.Collections.Generic.List'
    private static string StripArity(string typeName) {
        var arityIndex = typeName.LastIndexOf('`');
        return arityIndex < 0 ? typeName : typeName.Substring(0, arityIndex);
    }
}
