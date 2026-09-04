using System.Reflection;
using System.Reflection.Metadata;

namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE.MetadataDecoder: decodes a module's signatures into symbols
internal static class InternalMetadataDecoder {
    public static Type Type { get; }
    private static readonly Type peModuleSymbolType;
    private static readonly Type peMethodSymbolType;
    private static readonly ConstructorInfo constructor;
    private static readonly MethodInfo getLocalInfoMethod;

    static InternalMetadataDecoder() {
        Type = RoslynReflection.GetType(RoslynReflection.CSharpAssembly, "Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE.MetadataDecoder");
        peModuleSymbolType = RoslynReflection.GetType(RoslynReflection.CSharpAssembly, "Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE.PEModuleSymbol");
        peMethodSymbolType = RoslynReflection.GetType(RoslynReflection.CSharpAssembly, "Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE.PEMethodSymbol");
        constructor = RoslynReflection.GetConstructor(Type, peModuleSymbolType, peMethodSymbolType);
        // Declared on the generic base decoder of Microsoft.CodeAnalysis
        getLocalInfoMethod = RoslynReflection.GetMethod(Type, "GetLocalInfo", typeof(StandaloneSignatureHandle));
    }

    public static object Create(object peModule, object peMethod) {
        return constructor.Invoke([peModule, peMethod]);
    }
    // The ImmutableArray<LocalInfo<TypeSymbol>> of the method's local signature, empty for a nil handle
    public static object GetLocalInfo(object decoder, StandaloneSignatureHandle localSignature) {
        return getLocalInfoMethod.Invoke(decoder, [localSignature])!;
    }
}
