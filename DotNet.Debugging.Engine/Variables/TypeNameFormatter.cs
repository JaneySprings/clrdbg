using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;

namespace DotNet.Debugging.Engine.Variables;

// Formats runtime types as C# type names: 'int', 'string[]', 'List<int>', 'int?', 'Outer<string>.Nested'
internal static class TypeNameFormatter {
    private static readonly HashSet<string> primitiveTypeNames = new HashSet<string> {
        "bool", "byte", "sbyte", "char", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "nint", "nuint"
    };

    public static string GetTypeName(ICorDebugType type) {
        var elementType = type.GetElementType();
        var primitiveName = GetPrimitiveTypeName(elementType);
        if (primitiveName != null)
            return primitiveName;
        if (elementType == CorElementType.SZARRAY)
            return $"{GetTypeName(type.GetFirstTypeParameter())}[]";
        if (elementType == CorElementType.ARRAY)
            return $"{GetTypeName(type.GetFirstTypeParameter())}[{new string(',', type.GetRank() - 1)}]";

        // The type parameters may belong to the enclosing types of a nested class, e.g. for
        // Outer<string, int>.Nested<long> they are [string, int, long]: starting from the outermost
        // type, each level consumes the parameters its arity ('`1', '`2') asks for
        var typeParameters = type.GetTypeParameters().ToList();
        return GetClassName(type.GetClass(), typeParameters);
    }
    public static string? GetPrimitiveTypeName(CorElementType elementType) {
        return elementType switch {
            CorElementType.VOID => "void",
            CorElementType.BOOLEAN => "bool",
            CorElementType.CHAR => "char",
            CorElementType.I1 => "sbyte",
            CorElementType.U1 => "byte",
            CorElementType.I2 => "short",
            CorElementType.U2 => "ushort",
            CorElementType.I4 => "int",
            CorElementType.U4 => "uint",
            CorElementType.I8 => "long",
            CorElementType.U8 => "ulong",
            CorElementType.R4 => "float",
            CorElementType.R8 => "double",
            CorElementType.STRING => "string",
            CorElementType.OBJECT => "object",
            CorElementType.I => "nint",
            CorElementType.U => "nuint",
            _ => null
        };
    }
    public static bool IsPrimitiveTypeName(string typeName) {
        return primitiveTypeNames.Contains(typeName);
    }
    public static string ToLanguageAlias(string typeName) {
        return typeName switch {
            "System.String" => "string",
            "System.Object" => "object",
            "System.Decimal" => "decimal",
            // Seen when a primitive is boxed, e.g. object value = 4;
            "System.Boolean" => "bool",
            "System.Byte" => "byte",
            "System.SByte" => "sbyte",
            "System.Char" => "char",
            "System.Int16" => "short",
            "System.UInt16" => "ushort",
            "System.Int32" => "int",
            "System.UInt32" => "uint",
            "System.Int64" => "long",
            "System.UInt64" => "ulong",
            "System.Single" => "float",
            "System.Double" => "double",
            "System.IntPtr" => "nint",
            "System.UIntPtr" => "nuint",
            _ => typeName
        };
    }

    private static string GetClassName(ICorDebugClass corClass, List<ICorDebugType> typeParameters) {
        var module = corClass.GetModule();
        var token = corClass.GetToken();
        var metadataImport = module.GetMetaDataInterface<IMetaDataImport>();
        var typeDefProps = metadataImport.GetTypeDefProps(token);
        var typeName = typeDefProps.szTypeDef;

        string? enclosingTypeName = null;
        if (typeDefProps.pdwTypeDefFlags.IsTdNested()) {
            var enclosingClass = module.GetClassFromToken(metadataImport.GetNestedClassProps(token));
            enclosingTypeName = GetClassName(enclosingClass, typeParameters);
        }

        var arityIndex = typeName.LastIndexOf('`');
        if (arityIndex >= 0) {
            if (!int.TryParse(typeName.AsSpan(arityIndex + 1), out var arity))
                throw new InvalidOperationException($"Cannot parse the generic arity of '{typeName}'");
            var argumentNames = typeParameters.Take(arity).Select(GetTypeName).ToList();
            typeParameters.RemoveRange(0, Math.Min(arity, typeParameters.Count));
            typeName = $"{typeName.Substring(0, arityIndex)}<{string.Join(", ", argumentNames)}>";
        }

        // System.Nullable<int> -> int?
        if (typeName.StartsWith("System.Nullable<", StringComparison.Ordinal))
            typeName = string.Concat(typeName.AsSpan("System.Nullable<".Length, typeName.Length - "System.Nullable<".Length - 1), "?");

        var alias = ToLanguageAlias(typeName);
        return enclosingTypeName == null ? alias : $"{enclosingTypeName}.{alias}";
    }
}
