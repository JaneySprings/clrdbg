using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;

namespace DotNet.Debugging.Engine.Extensions;

// Helpers over the System.Reflection.Metadata readers of a module's PE metadata
internal static class MetadataReaderExtensions {
    // 'Namespace.Outer.Nested', the form types are displayed in
    public static string GetTypeName(this MetadataReader reader, TypeDefinitionHandle handle) {
        return GetQualifiedName(reader, handle, '.');
    }
    public static string GetTypeName(this MetadataReader reader, TypeReferenceHandle handle) {
        return GetQualifiedName(reader, handle, '.');
    }
    // 'Namespace.Outer+Nested', the reflection name a nested type is referenced by from another module
    public static string GetFullName(this MetadataReader reader, TypeDefinitionHandle handle) {
        return GetQualifiedName(reader, handle, '+');
    }
    public static string GetFullName(this MetadataReader reader, TypeReferenceHandle handle) {
        return GetQualifiedName(reader, handle, '+');
    }
    // 'string[] args, int count': the parameter types formatted by 'typeProvider', named after the Param rows
    public static string GetParameterList(this MetadataReader reader, int methodToken, ISignatureTypeProvider<string, object?> typeProvider) {
        try {
            var method = reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(methodToken));
            var parameterTypes = method.DecodeSignature(typeProvider, null).ParameterTypes;
            // Sequence 0 is the return value, the rest are the 1-based positional parameters
            var parameterNames = method.GetParameters()
                .Select(reader.GetParameter)
                .Where(it => it.SequenceNumber > 0)
                .OrderBy(it => it.SequenceNumber)
                .Select(it => reader.GetString(it.Name))
                .ToList();
            var parameters = parameterTypes.Select((type, index) => index < parameterNames.Count ? $"{type} {parameterNames[index]}" : type);
            return string.Join(", ", parameters);
        }
        catch {
            return string.Empty;
        }
    }

    public static bool TryFindTypeDefinition(this MetadataReader reader, string @namespace, string name, out TypeDefinitionHandle handle) {
        foreach (var typeHandle in reader.TypeDefinitions) {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) == name && reader.GetString(type.Namespace) == @namespace) {
                handle = typeHandle;
                return true;
            }
        }
        handle = default;
        return false;
    }
    // The first method of the name declared by a type of the name (a nested type's name is its own)
    public static bool TryFindMethodDefinition(this MetadataReader reader, string typeName, string methodName, out MethodDefinitionHandle handle) {
        foreach (var typeHandle in reader.TypeDefinitions) {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != typeName)
                continue;
            foreach (var methodHandle in type.GetMethods()) {
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) == methodName) {
                    handle = methodHandle;
                    return true;
                }
            }
        }
        handle = default;
        return false;
    }
    // Whether the type extends System.ValueType or System.Enum
    public static bool IsValueType(this MetadataReader reader, TypeDefinitionHandle handle) {
        var definition = reader.GetTypeDefinition(handle);
        switch (definition.BaseType.Kind) {
            // The core library defines System.ValueType / System.Enum itself, so its base types are definitions rather than references
            case HandleKind.TypeDefinition:
                var baseDefinition = reader.GetTypeDefinition((TypeDefinitionHandle)definition.BaseType);
                return IsSystemValueType(reader, baseDefinition.Namespace, baseDefinition.Name);
            case HandleKind.TypeReference:
                var baseReference = reader.GetTypeReference((TypeReferenceHandle)definition.BaseType);
                return IsSystemValueType(reader, baseReference.Namespace, baseReference.Name);
            default:
                return false;
        }
    }
    // Whether the assembly the reader holds is the one an assembly reference names, the version only when both carry one
    public static bool MatchesAssemblyIdentity(this MetadataReader reader, AssemblyDefinition assembly, Version? version, string culture, byte[]? publicKeyOrToken) {
        if (version != null && assembly.Version != null && version != assembly.Version)
            return false;
        if (!string.Equals(culture, reader.GetString(assembly.Culture), StringComparison.OrdinalIgnoreCase))
            return false;
        return PublicKeysMatch(publicKeyOrToken, assembly.PublicKey.IsNil ? null : reader.GetBlobBytes(assembly.PublicKey));
    }
    public static bool SignatureEquals(this MethodSignature<string> left, MethodSignature<string> right) {
        return left.GenericParameterCount == right.GenericParameterCount
            && left.ParameterTypes.SequenceEqual(right.ParameterTypes)
            && left.ReturnType == right.ReturnType;
    }

    private static string GetQualifiedName(MetadataReader reader, TypeDefinitionHandle handle, char nestedSeparator) {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        var declaringType = type.GetDeclaringType();
        if (!declaringType.IsNil)
            return $"{GetQualifiedName(reader, declaringType, nestedSeparator)}{nestedSeparator}{name}";
        return JoinNamespace(reader.GetString(type.Namespace), name);
    }
    private static string GetQualifiedName(MetadataReader reader, TypeReferenceHandle handle, char nestedSeparator) {
        var type = reader.GetTypeReference(handle);
        var name = reader.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            return $"{GetQualifiedName(reader, (TypeReferenceHandle)type.ResolutionScope, nestedSeparator)}{nestedSeparator}{name}";
        return JoinNamespace(reader.GetString(type.Namespace), name);
    }
    private static string JoinNamespace(string ns, string name) {
        return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    }
    private static bool IsSystemValueType(MetadataReader reader, StringHandle @namespace, StringHandle name) {
        return reader.GetString(@namespace) == "System" && reader.GetString(name) is "ValueType" or "Enum";
    }
    private static bool PublicKeysMatch(byte[]? referencedToken, byte[]? definitionKey) {
        if (referencedToken == null || referencedToken.Length == 0 || definitionKey == null || definitionKey.Length == 0)
            return true;
        if (referencedToken.Length == 8 && definitionKey.Length > 8) {
            // The assembly reference carries the public key token: the last 8 bytes of the SHA1 of the full public key, reversed
#pragma warning disable CA5350 // The token format is defined by the runtime
            var hash = SHA1.HashData(definitionKey);
#pragma warning restore CA5350
            Array.Reverse(hash);
            return referencedToken.AsSpan().SequenceEqual(hash.AsSpan(0, 8));
        }
        return referencedToken.AsSpan().SequenceEqual(definitionKey);
    }
}
