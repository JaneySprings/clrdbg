using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine.Evaluation;

internal class ResolvedRuntimeType {
    public ModuleInfo Module { get; }
    public TypeDefinitionHandle Handle { get; }
    public ImmutableArray<ResolvedCilType> TypeArguments { get; }
    public ICorDebugClass Class => Module.Module.GetClassFromToken((TypeDefToken)MetadataTokens.GetToken(Handle));

    public ResolvedRuntimeType(ModuleInfo module, TypeDefinitionHandle handle, ImmutableArray<ResolvedCilType> typeArguments = default) {
        Module = module;
        Handle = handle;
        TypeArguments = typeArguments;
    }

    public ResolvedRuntimeType WithTypeArguments(ImmutableArray<ResolvedCilType> typeArguments) {
        return new ResolvedRuntimeType(Module, Handle, typeArguments);
    }
}

internal class ResolvedRuntimeField {
    public ResolvedRuntimeType DeclaringType { get; }
    public FieldDefinitionHandle Handle { get; }
    public bool IsStatic { get; }
    public FieldDefToken Token => (FieldDefToken)MetadataTokens.GetToken(Handle);

    public ResolvedRuntimeField(ResolvedRuntimeType declaringType, FieldDefinitionHandle handle, bool isStatic) {
        DeclaringType = declaringType;
        Handle = handle;
        IsStatic = isStatic;
    }
}

internal class ResolvedRuntimeMethod {
    public ResolvedRuntimeType DeclaringType { get; }
    public MethodDefinitionHandle Handle { get; }
    public string Name { get; }
    public MethodSignature<string> Signature { get; }
    public bool IsStatic { get; }
    public ImmutableArray<ResolvedCilType> MethodTypeArguments { get; }
    public ICorDebugFunction Function => DeclaringType.Module.Module.GetFunctionFromToken((MethodDefToken)MetadataTokens.GetToken(Handle));

    public ResolvedRuntimeMethod(ResolvedRuntimeType declaringType, MethodDefinitionHandle handle, string name, MethodSignature<string> signature, bool isStatic, ImmutableArray<ResolvedCilType> methodTypeArguments = default) {
        DeclaringType = declaringType;
        Handle = handle;
        Name = name;
        Signature = signature;
        IsStatic = isStatic;
        MethodTypeArguments = methodTypeArguments;
    }
}

// A method defined in the evaluation assembly itself (a lambda or local function of the expression)
internal class ResolvedEvaluationMethod {
    public MethodDefinitionHandle Handle { get; }
    public MethodSignature<string> Signature { get; }
    public bool IsStatic { get; }

    public ResolvedEvaluationMethod(MethodDefinitionHandle handle, MethodSignature<string> signature, bool isStatic) {
        Handle = handle;
        Signature = signature;
        IsStatic = isStatic;
    }
}

// A type as the interpreter sees it: a primitive, a runtime type or an array of either
internal class ResolvedCilType {
    public PrimitiveTypeCode? Primitive { get; }
    public ResolvedRuntimeType? RuntimeType { get; }
    public ResolvedCilType? ElementType { get; }
    public int ArrayRank { get; }
    public bool IsSzArray { get; }

    public ResolvedCilType(PrimitiveTypeCode? primitive, ResolvedRuntimeType? runtimeType, ResolvedCilType? elementType = null, int arrayRank = 0, bool isSzArray = false) {
        Primitive = primitive;
        RuntimeType = runtimeType;
        ElementType = elementType;
        ArrayRank = arrayRank;
        IsSzArray = isSzArray;
    }

    public static ResolvedCilType FromPrimitive(PrimitiveTypeCode primitive) {
        return new ResolvedCilType(primitive, null);
    }
    public static ResolvedCilType FromRuntimeType(ResolvedRuntimeType runtimeType) {
        return new ResolvedCilType(null, runtimeType);
    }
    public static ResolvedCilType FromArray(ResolvedCilType elementType, int rank, bool isSzArray) {
        return new ResolvedCilType(null, null, elementType, rank, isSzArray);
    }
}

// Maps the tokens of the evaluation assembly (type/member references into the debuggee's assemblies) to the loaded modules
internal class EvaluationMetadataResolver {
    private readonly ManagedDebugger debugger;
    private readonly MetadataReader evaluationReader;
    private readonly PEReader evaluationPeReader;
    private readonly ICorDebugAppDomain appDomain;
    // The concrete generic arguments the evaluation method's '!i' type parameters and '!!i' method type parameters map to.
    // For a frame evaluation these are the frame's instantiation, for a type-context (DebuggerDisplay) evaluation the root value's type arguments
    private readonly ICorDebugType[] typeGenericArguments;
    private readonly ICorDebugType[] methodGenericArguments;
    private readonly ModuleInfo? preferredModule;
    private readonly Dictionary<int, ResolvedRuntimeField> fieldCache = new Dictionary<int, ResolvedRuntimeField>();
    private readonly Dictionary<int, ResolvedRuntimeMethod> methodCache = new Dictionary<int, ResolvedRuntimeMethod>();
    private readonly Dictionary<int, ResolvedCilType> typeTokenCache = new Dictionary<int, ResolvedCilType>();
    private readonly Dictionary<string, ResolvedRuntimeMethod> runtimeMethodCache = new Dictionary<string, ResolvedRuntimeMethod>();
    private readonly Dictionary<string, ResolvedRuntimeType> runtimeTypeCache = new Dictionary<string, ResolvedRuntimeType>();

    public EvaluationMetadataResolver(ManagedDebugger debugger, CompiledExpression compiled, ICorDebugAppDomain appDomain, ICorDebugType[] typeGenericArguments, ICorDebugType[] methodGenericArguments, ModuleInfo? preferredModule) {
        this.debugger = debugger;
        evaluationReader = compiled.MetadataReader;
        evaluationPeReader = compiled.PeReader;
        this.appDomain = appDomain;
        this.typeGenericArguments = typeGenericArguments;
        this.methodGenericArguments = methodGenericArguments;
        this.preferredModule = preferredModule;
    }

    public string ResolveUserString(int token) {
        return evaluationReader.GetUserString(MetadataTokens.UserStringHandle(token));
    }
    public ResolvedCilType ResolveMethodReturnType(MethodDefinitionHandle handle) {
        return evaluationReader.GetMethodDefinition(handle).DecodeSignature(new RuntimeTypeSignatureProvider(this), genericContext: null).ReturnType;
    }
    public ResolvedCilType ResolveTypeToken(int token) {
        if (typeTokenCache.TryGetValue(token, out var cached))
            return cached;

        var handle = MetadataTokens.EntityHandle(token);
        var result = handle.Kind == HandleKind.TypeSpecification
            ? evaluationReader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(new RuntimeTypeSignatureProvider(this), genericContext: null)
            : ResolvedCilType.FromRuntimeType(ResolveType(handle));
        typeTokenCache[token] = result;
        return result;
    }
    public ResolvedCilType ResolveGenericTypeParameter(int index) {
        return ResolveGenericParameter(index, typeGenericArguments, "!");
    }
    public ResolvedCilType ResolveGenericMethodParameter(int index) {
        return ResolveGenericParameter(index, methodGenericArguments, "!!");
    }
    public ResolvedRuntimeField ResolveField(int token) {
        if (fieldCache.TryGetValue(token, out var cached))
            return cached;

        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind != HandleKind.MemberReference)
            throw new NotSupportedException($"Evaluation field token kind '{handle.Kind}' is not supported");

        var member = evaluationReader.GetMemberReference((MemberReferenceHandle)handle);
        var declaringType = ResolveType(member.Parent);
        var name = evaluationReader.GetString(member.Name);
        var reader = declaringType.Module.MetadataReader.PeMetadataReader;
        foreach (var fieldHandle in reader.GetTypeDefinition(declaringType.Handle).GetFields()) {
            var field = reader.GetFieldDefinition(fieldHandle);
            if (reader.GetString(field.Name) != name)
                continue;
            var result = new ResolvedRuntimeField(declaringType, fieldHandle, (field.Attributes & FieldAttributes.Static) != 0);
            fieldCache[token] = result;
            return result;
        }
        throw new MissingFieldException(GetTypeName(declaringType), name);
    }
    public ResolvedRuntimeMethod ResolveMethod(int token) {
        if (methodCache.TryGetValue(token, out var cached))
            return cached;

        var handle = MetadataTokens.EntityHandle(token);
        var methodTypeArguments = default(ImmutableArray<ResolvedCilType>);
        if (handle.Kind == HandleKind.MethodSpecification) {
            var specification = evaluationReader.GetMethodSpecification((MethodSpecificationHandle)handle);
            methodTypeArguments = specification.DecodeSignature(new RuntimeTypeSignatureProvider(this), genericContext: null);
            handle = specification.Method;
        }
        if (handle.Kind != HandleKind.MemberReference)
            throw new NotSupportedException($"Evaluation method token kind '{handle.Kind}' is not supported");

        var member = evaluationReader.GetMemberReference((MemberReferenceHandle)handle);
        var declaringType = ResolveType(member.Parent);
        var name = evaluationReader.GetString(member.Name);
        var expectedSignature = member.DecodeMethodSignature(SignatureNameProvider.Instance, genericContext: null);
        var reader = declaringType.Module.MetadataReader.PeMetadataReader;
        foreach (var methodHandle in reader.GetTypeDefinition(declaringType.Handle).GetMethods()) {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != name)
                continue;
            var signature = method.DecodeSignature(SignatureNameProvider.Instance, genericContext: null);
            if (!SignaturesEqual(expectedSignature, signature))
                continue;
            var result = new ResolvedRuntimeMethod(declaringType, methodHandle, name, signature, (method.Attributes & MethodAttributes.Static) != 0, methodTypeArguments);
            methodCache[token] = result;
            return result;
        }
        throw new MissingMethodException(GetTypeName(declaringType), name);
    }
    public bool TryResolveEvaluationMethod(int token, out ResolvedEvaluationMethod result) {
        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind == HandleKind.MethodSpecification)
            handle = evaluationReader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
        if (handle.Kind != HandleKind.MethodDefinition) {
            result = null!;
            return false;
        }

        var methodHandle = (MethodDefinitionHandle)handle;
        var method = evaluationReader.GetMethodDefinition(methodHandle);
        result = new ResolvedEvaluationMethod(methodHandle, method.DecodeSignature(SignatureNameProvider.Instance, genericContext: null), (method.Attributes & MethodAttributes.Static) != 0);
        return true;
    }
    public bool TryResolveDebuggerIntrinsic(int token, out string methodName) {
        methodName = string.Empty;
        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind == HandleKind.MethodSpecification)
            handle = evaluationReader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
        if (handle.Kind != HandleKind.MemberReference)
            return false;

        var member = evaluationReader.GetMemberReference((MemberReferenceHandle)handle);
        if (member.Parent.Kind != HandleKind.TypeReference)
            return false;

        var type = evaluationReader.GetTypeReference((TypeReferenceHandle)member.Parent);
        if (evaluationReader.GetString(type.Namespace) != "Microsoft.VisualStudio.Debugger.Clr" || evaluationReader.GetString(type.Name) != "IntrinsicMethods")
            return false;

        methodName = evaluationReader.GetString(member.Name);
        return true;
    }
    public MethodBodyBlock GetEvaluationMethodBody(MethodDefinitionHandle handle) {
        return evaluationPeReader.GetMethodBody(evaluationReader.GetMethodDefinition(handle).RelativeVirtualAddress);
    }
    public int GetEvaluationLocalCount(StandaloneSignatureHandle handle) {
        if (handle.IsNil)
            return 0;
        return evaluationReader.GetStandaloneSignature(handle).DecodeLocalSignature(LocalCountSignatureProvider.Instance, genericContext: null).Length;
    }

    public ICorDebugType GetCorDebugType(ResolvedRuntimeType type) {
        var elementType = IsValueType(type) ? CorElementType.VALUETYPE : CorElementType.CLASS;
        var typeArguments = type.TypeArguments.IsDefaultOrEmpty ? [] : type.TypeArguments.Select(GetCorDebugType).ToArray();
        return ((ICorDebugClass2)type.Class).GetParameterizedType(elementType, typeArguments.Length, typeArguments);
    }
    public ICorDebugType GetCorDebugType(ResolvedCilType type) {
        if (type.ElementType != null) {
            var arrayKind = type.IsSzArray ? CorElementType.SZARRAY : CorElementType.ARRAY;
            return ((ICorDebugAppDomain2)appDomain).GetArrayOrPointerType(arrayKind, type.ArrayRank, GetCorDebugType(type.ElementType));
        }
        if (type.RuntimeType != null)
            return GetCorDebugType(type.RuntimeType);
        if (type.Primitive == null)
            throw new NotSupportedException("The CIL type cannot be materialized");

        var typeName = GetPrimitiveTypeName(type.Primitive.Value);
        var isClass = type.Primitive == PrimitiveTypeCode.String || type.Primitive == PrimitiveTypeCode.Object;
        var runtimeType = FindRuntimeType("System", typeName.Substring("System.".Length));
        return ((ICorDebugClass2)runtimeType.Class).GetParameterizedType(isClass ? CorElementType.CLASS : CorElementType.VALUETYPE, 0, []);
    }
    public string GetRuntimeTypeName(ResolvedRuntimeType type) {
        return GetTypeName(type);
    }
    public int GetRuntimeTypeGenericArity(ResolvedRuntimeType type) {
        return type.Module.MetadataReader.PeMetadataReader.GetTypeDefinition(type.Handle).GetGenericParameters().Count;
    }
    public string GetAssemblyQualifiedTypeName(ResolvedCilType type) {
        return $"{GetReflectionTypeName(type)}, {GetTypeAssemblyName(type)}";
    }
    public ResolvedRuntimeMethod ResolveRuntimeMethod(string @namespace, string typeName, string methodName, params string[] parameterTypes) {
        var cacheKey = $"{@namespace}.{typeName}.{methodName}({string.Join(",", parameterTypes)})";
        if (runtimeMethodCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var declaringType = FindRuntimeType(@namespace, typeName);
        var reader = declaringType.Module.MetadataReader.PeMetadataReader;
        foreach (var handle in reader.GetTypeDefinition(declaringType.Handle).GetMethods()) {
            var method = reader.GetMethodDefinition(handle);
            if (reader.GetString(method.Name) != methodName)
                continue;
            var signature = method.DecodeSignature(SignatureNameProvider.Instance, genericContext: null);
            if (!signature.ParameterTypes.SequenceEqual(parameterTypes))
                continue;
            var result = new ResolvedRuntimeMethod(declaringType, handle, methodName, signature, (method.Attributes & MethodAttributes.Static) != 0);
            runtimeMethodCache[cacheKey] = result;
            return result;
        }
        throw new MissingMethodException($"{@namespace}.{typeName}", methodName);
    }

    public static bool IsValueType(ResolvedRuntimeType type) {
        var reader = type.Module.MetadataReader.PeMetadataReader;
        var definition = reader.GetTypeDefinition(type.Handle);
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

    private ResolvedCilType ResolveGenericParameter(int index, ICorDebugType[] arguments, string prefix) {
        if (index >= 0 && index < arguments.Length)
            return ResolveCorDebugType(arguments[index]);
        throw new NotSupportedException($"The generic parameter '{prefix}{index}' is not available in the current frame");
    }
    private ResolvedCilType ResolveCorDebugType(ICorDebugType type) {
        var elementType = type.GetElementType();
        switch (elementType) {
            case CorElementType.CLASS:
            case CorElementType.VALUETYPE:
                return ResolvedCilType.FromRuntimeType(ResolveRuntimeType(type));
            case CorElementType.SZARRAY:
                return ResolvedCilType.FromArray(ResolveCorDebugType(type.GetFirstTypeParameter()), 1, true);
            case CorElementType.ARRAY:
                return ResolvedCilType.FromArray(ResolveCorDebugType(type.GetFirstTypeParameter()), type.GetRank(), false);
        }
        var primitive = GetPrimitiveTypeCode(elementType);
        if (primitive == null)
            throw new NotSupportedException($"Cannot resolve a CIL type from CorElementType '{elementType}'");
        return ResolvedCilType.FromPrimitive(primitive.Value);
    }
    private ResolvedRuntimeType ResolveRuntimeType(ICorDebugType type) {
        var corClass = type.GetClass();
        var moduleInfo = debugger.GetModule(corClass.GetModule());
        var handle = (TypeDefinitionHandle)MetadataTokens.Handle(corClass.GetToken());
        ICorDebugType[] typeParameters;
        try {
            typeParameters = type.GetTypeParameters();
        }
        catch {
            typeParameters = [];
        }
        var typeArguments = typeParameters.Length == 0 ? default : typeParameters.Select(ResolveCorDebugType).ToImmutableArray();
        return new ResolvedRuntimeType(moduleInfo, handle, typeArguments);
    }
    private ResolvedRuntimeType ResolveType(EntityHandle handle) {
        switch (handle.Kind) {
            case HandleKind.TypeReference:
                return ResolveTypeReference((TypeReferenceHandle)handle);
            case HandleKind.TypeSpecification:
                var type = evaluationReader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(new RuntimeTypeSignatureProvider(this), genericContext: null);
                return type.RuntimeType ?? throw new TypeLoadException("The type specification does not identify a runtime type");
            default:
                throw new NotSupportedException($"Evaluation type token kind '{handle.Kind}' is not supported");
        }
    }
    private ResolvedRuntimeType ResolveTypeReference(TypeReferenceHandle handle) {
        var reference = evaluationReader.GetTypeReference(handle);
        var name = evaluationReader.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference) {
            var containing = ResolveTypeReference((TypeReferenceHandle)reference.ResolutionScope);
            var reader = containing.Module.MetadataReader.PeMetadataReader;
            foreach (var nestedHandle in reader.GetTypeDefinition(containing.Handle).GetNestedTypes()) {
                if (reader.GetString(reader.GetTypeDefinition(nestedHandle).Name) == name)
                    return new ResolvedRuntimeType(containing.Module, nestedHandle);
            }
            throw new TypeLoadException($"Nested type '{name}' was not found in '{GetTypeName(containing)}'");
        }

        var @namespace = evaluationReader.GetString(reference.Namespace);
        if (reference.ResolutionScope.Kind == HandleKind.AssemblyReference) {
            var assemblyReference = (AssemblyReferenceHandle)reference.ResolutionScope;
            foreach (var module in FindModules(assemblyReference)) {
                if (TryFindType(module, @namespace, name, out var typeHandle))
                    return new ResolvedRuntimeType(module, typeHandle);
            }
            var assemblyName = evaluationReader.GetString(evaluationReader.GetAssemblyReference(assemblyReference).Name);
            throw new TypeLoadException($"Type '{@namespace}.{name}' from assembly '{assemblyName}' is not loaded");
        }

        foreach (var module in FindModules(assemblyName: null)) {
            if (TryFindType(module, @namespace, name, out var typeHandle))
                return new ResolvedRuntimeType(module, typeHandle);
        }
        throw new TypeLoadException($"Type '{@namespace}.{name}' is not loaded");
    }
    private static bool TryFindType(ModuleInfo module, string @namespace, string name, out TypeDefinitionHandle handle) {
        var reader = module.MetadataReader.PeMetadataReader;
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

    // Resolves an assembly reference of the evaluation assembly to the loaded modules, preferring the module the
    // evaluation was compiled against. When several modules share the same assembly identity (the same assembly
    // in several AssemblyLoadContexts) that one is the instance the user is debugging and the one Roslyn bound
    // the expression against (see ExpressionCompiler.GetMetadataBlocks)
    private List<ModuleInfo> FindModules(AssemblyReferenceHandle assemblyReference) {
        var reference = evaluationReader.GetAssemblyReference(assemblyReference);
        var name = evaluationReader.GetString(reference.Name);
        var culture = evaluationReader.GetString(reference.Culture);
        var publicKeyOrToken = reference.PublicKeyOrToken.IsNil ? null : evaluationReader.GetBlobBytes(reference.PublicKeyOrToken);

        var matches = new List<ModuleInfo>();
        foreach (var module in debugger.Modules) {
            var reader = module.MetadataReader.PeMetadataReader;
            if (!reader.IsAssembly)
                continue;
            var assembly = reader.GetAssemblyDefinition();
            if (reader.GetString(assembly.Name) != name || !MatchesIdentity(reader, assembly, reference.Version, culture, publicKeyOrToken))
                continue;
            if (module == preferredModule)
                matches.Insert(0, module);
            else
                matches.Add(module);
        }
        // The referenced identity matches no loaded module (a binding redirect or a version mismatch), fall back to the simple name
        return matches.Count > 0 ? matches : FindModules(name);
    }
    private List<ModuleInfo> FindModules(string? assemblyName) {
        var matches = new List<ModuleInfo>();
        foreach (var module in debugger.Modules) {
            var reader = module.MetadataReader.PeMetadataReader;
            if (assemblyName != null && (!reader.IsAssembly || reader.GetString(reader.GetAssemblyDefinition().Name) != assemblyName))
                continue;
            if (module == preferredModule)
                matches.Insert(0, module);
            else
                matches.Add(module);
        }
        return matches;
    }
    private static bool MatchesIdentity(MetadataReader reader, AssemblyDefinition assembly, Version? version, string culture, byte[]? publicKeyOrToken) {
        if (version != null && assembly.Version != null && version != assembly.Version)
            return false;
        if (!string.Equals(culture, reader.GetString(assembly.Culture), StringComparison.OrdinalIgnoreCase))
            return false;
        return PublicKeysMatch(publicKeyOrToken, assembly.PublicKey.IsNil ? null : reader.GetBlobBytes(assembly.PublicKey));
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
    private ResolvedRuntimeType FindRuntimeType(string @namespace, string name) {
        var cacheKey = $"{@namespace}.{name}";
        if (runtimeTypeCache.TryGetValue(cacheKey, out var cached))
            return cached;

        foreach (var module in FindModules(assemblyName: null)) {
            if (!TryFindType(module, @namespace, name, out var handle))
                continue;
            var result = new ResolvedRuntimeType(module, handle);
            runtimeTypeCache[cacheKey] = result;
            return result;
        }
        throw new TypeLoadException($"Type '{@namespace}.{name}' is not loaded");
    }

    private static bool IsSystemValueType(MetadataReader reader, StringHandle @namespace, StringHandle name) {
        return reader.GetString(@namespace) == "System" && reader.GetString(name) is "ValueType" or "Enum";
    }
    private static string GetTypeName(ResolvedRuntimeType type) {
        var reader = type.Module.MetadataReader.PeMetadataReader;
        var definition = reader.GetTypeDefinition(type.Handle);
        var name = reader.GetString(definition.Name);
        var declaringType = definition.GetDeclaringType();
        if (!declaringType.IsNil)
            return $"{GetTypeName(new ResolvedRuntimeType(type.Module, declaringType))}+{name}";
        var @namespace = reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
    }
    private string GetReflectionTypeName(ResolvedCilType type) {
        if (type.ElementType != null) {
            var suffix = type.IsSzArray ? "[]" : $"[{new string(',', type.ArrayRank - 1)}]";
            return GetReflectionTypeName(type.ElementType) + suffix;
        }
        if (type.Primitive != null)
            return GetPrimitiveTypeName(type.Primitive.Value);

        var runtimeType = type.RuntimeType ?? throw new TypeLoadException("The CIL type is unresolved");
        var name = GetTypeName(runtimeType);
        if (!runtimeType.TypeArguments.IsDefaultOrEmpty)
            name += $"[[{string.Join("],[", runtimeType.TypeArguments.Select(GetAssemblyQualifiedTypeName))}]]";
        return name;
    }
    private string GetTypeAssemblyName(ResolvedCilType type) {
        if (type.ElementType != null)
            return GetTypeAssemblyName(type.ElementType);
        if (type.Primitive != null)
            return "System.Private.CoreLib";

        var runtimeType = type.RuntimeType ?? throw new TypeLoadException("The CIL type is unresolved");
        var reader = runtimeType.Module.MetadataReader.PeMetadataReader;
        return reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : Path.GetFileNameWithoutExtension(runtimeType.Module.Name);
    }
    private static string GetPrimitiveTypeName(PrimitiveTypeCode primitive) {
        return primitive switch {
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Char => "System.Char",
            PrimitiveTypeCode.Int16 => "System.Int16",
            PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.UInt32 => "System.UInt32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.Object => "System.Object",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            _ => throw new NotSupportedException($"Primitive type '{primitive}' is not supported")
        };
    }
    private static PrimitiveTypeCode? GetPrimitiveTypeCode(CorElementType elementType) {
        return elementType switch {
            CorElementType.BOOLEAN => PrimitiveTypeCode.Boolean,
            CorElementType.CHAR => PrimitiveTypeCode.Char,
            CorElementType.I1 => PrimitiveTypeCode.SByte,
            CorElementType.U1 => PrimitiveTypeCode.Byte,
            CorElementType.I2 => PrimitiveTypeCode.Int16,
            CorElementType.U2 => PrimitiveTypeCode.UInt16,
            CorElementType.I4 => PrimitiveTypeCode.Int32,
            CorElementType.U4 => PrimitiveTypeCode.UInt32,
            CorElementType.I8 => PrimitiveTypeCode.Int64,
            CorElementType.U8 => PrimitiveTypeCode.UInt64,
            CorElementType.R4 => PrimitiveTypeCode.Single,
            CorElementType.R8 => PrimitiveTypeCode.Double,
            CorElementType.I => PrimitiveTypeCode.IntPtr,
            CorElementType.U => PrimitiveTypeCode.UIntPtr,
            CorElementType.STRING => PrimitiveTypeCode.String,
            CorElementType.OBJECT => PrimitiveTypeCode.Object,
            _ => null
        };
    }
    private static bool SignaturesEqual(MethodSignature<string> left, MethodSignature<string> right) {
        return left.GenericParameterCount == right.GenericParameterCount
            && left.ParameterTypes.SequenceEqual(right.ParameterTypes)
            && left.ReturnType == right.ReturnType;
    }

    // Formats signature types as full metadata names, for comparing a referenced signature with the definitions
    private sealed class SignatureNameProvider : ISignatureTypeProvider<string, object?> {
        public static SignatureNameProvider Instance { get; } = new SignatureNameProvider();

        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => $"{genericType}<{string.Join(",", typeArguments)}>";
        public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
        public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => GetFullName(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => GetFullName(reader, handle);
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }

        private static string GetFullName(MetadataReader reader, TypeDefinitionHandle handle) {
            var type = reader.GetTypeDefinition(handle);
            var @namespace = reader.GetString(type.Namespace);
            var name = reader.GetString(type.Name);
            return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
        }
        private static string GetFullName(MetadataReader reader, TypeReferenceHandle handle) {
            var type = reader.GetTypeReference(handle);
            var name = reader.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
                return $"{GetFullName(reader, (TypeReferenceHandle)type.ResolutionScope)}+{name}";
            var @namespace = reader.GetString(type.Namespace);
            return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
        }
    }

    private sealed class RuntimeTypeSignatureProvider : ISignatureTypeProvider<ResolvedCilType, object?> {
        private readonly EvaluationMetadataResolver resolver;

        public RuntimeTypeSignatureProvider(EvaluationMetadataResolver resolver) {
            this.resolver = resolver;
        }

        public ResolvedCilType GetPrimitiveType(PrimitiveTypeCode typeCode) => ResolvedCilType.FromPrimitive(typeCode);
        public ResolvedCilType GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => ResolvedCilType.FromRuntimeType(resolver.ResolveType(handle));
        public ResolvedCilType GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => ResolvedCilType.FromRuntimeType(resolver.ResolveType(handle));
        public ResolvedCilType GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }
        public ResolvedCilType GetGenericInstantiation(ResolvedCilType genericType, ImmutableArray<ResolvedCilType> typeArguments) {
            if (genericType.RuntimeType == null)
                throw new TypeLoadException("A generic instantiation must identify a runtime type");
            return ResolvedCilType.FromRuntimeType(genericType.RuntimeType.WithTypeArguments(typeArguments));
        }
        public ResolvedCilType GetArrayType(ResolvedCilType elementType, ArrayShape shape) => ResolvedCilType.FromArray(elementType, shape.Rank, false);
        public ResolvedCilType GetSZArrayType(ResolvedCilType elementType) => ResolvedCilType.FromArray(elementType, 1, true);
        public ResolvedCilType GetByReferenceType(ResolvedCilType elementType) => elementType;
        public ResolvedCilType GetPointerType(ResolvedCilType elementType) => elementType;
        public ResolvedCilType GetPinnedType(ResolvedCilType elementType) => elementType;
        public ResolvedCilType GetModifiedType(ResolvedCilType modifier, ResolvedCilType unmodifiedType, bool isRequired) => unmodifiedType;
        public ResolvedCilType GetFunctionPointerType(MethodSignature<ResolvedCilType> signature) => throw new NotSupportedException("Function pointer types are not supported");
        public ResolvedCilType GetGenericMethodParameter(object? genericContext, int index) => resolver.ResolveGenericMethodParameter(index);
        public ResolvedCilType GetGenericTypeParameter(object? genericContext, int index) => resolver.ResolveGenericTypeParameter(index);
    }
}
