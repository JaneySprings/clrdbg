using Microsoft.CodeAnalysis;

namespace DotNet.Debugging.Remote.Generator;

// What a parameter's type is on the wire, from its name in the declaration and the compilation for the CorApi types
internal sealed class TypeKinds {
    private readonly Compilation compilation;

    public TypeKinds(Compilation compilation) {
        this.compilation = compilation;
    }

    public string Categorize(CorApiParameter parameter) {
        if (parameter.IsPointer)
            return parameter.ElementTypeName == "byte" ? "bytepointer" : "unsupported";
        switch (parameter.ElementTypeName) {
            case "uint": return "u32";
            case "int": return "int";
            case "bool": return "bool";
            case "ulong": return "u64";
            case "nuint": return "nuint";
            case "nint": return "nint";
            case "string": return "text";
            case "char": return "chars";
            case "byte": return "bytes";
            case "object": return "unsupported";
            case "Guid": return "guid";
            case "CordbAddress": return "address";
            case "HCorEnum": return "hcorenum";
        }
        var type = compilation.GetTypeByMetadataName(CorApiInterface.Namespace + "." + parameter.ElementTypeName);
        if (type == null)
            return "unsupported";
        if (type.TypeKind == TypeKind.Interface)
            return "object";
        if (type.TypeKind == TypeKind.Enum)
            return "enum";
        if (type.TypeKind == TypeKind.Struct && type.Name.EndsWith("Token"))
            return "token";
        if (type.TypeKind == TypeKind.Struct)
            return "struct";
        return "unsupported";
    }
}
