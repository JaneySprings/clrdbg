using System.Reflection;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis.CSharp;

namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.CompilationExtensions: the compilation over the metadata blocks
// and the PE symbols (PEMethodSymbol, PENamedTypeSymbol) it resolves a module's tokens to
internal static class InternalCompilationExtensions {
    public static Type Type { get; }
    private static readonly MethodInfo toCompilationMethod;
    private static readonly MethodInfo getSourceMethodMethod;
    private static readonly MethodInfo getMethodMethod;
    private static readonly MethodInfo getTypeMethod;
    private static readonly Type makeAssemblyReferencesKindType;

    static InternalCompilationExtensions() {
        Type = RoslynReflection.GetType(RoslynReflection.CSharpExpressionCompilerAssembly, "Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.CompilationExtensions");
        makeAssemblyReferencesKindType = RoslynReflection.GetType(RoslynReflection.ExpressionCompilerAssembly, "Microsoft.CodeAnalysis.ExpressionEvaluator.MakeAssemblyReferencesKind");
        toCompilationMethod = RoslynReflection.GetMethod(Type, "ToCompilation", RoslynReflection.MakeImmutableArrayType(InternalMetadataBlock.Type), InternalModuleId.Type, makeAssemblyReferencesKindType);
        getSourceMethodMethod = RoslynReflection.GetMethod(Type, "GetSourceMethod", typeof(CSharpCompilation), InternalModuleId.Type, typeof(MethodDefinitionHandle));
        getMethodMethod = RoslynReflection.GetMethod(Type, "GetMethod", typeof(CSharpCompilation), InternalModuleId.Type, typeof(MethodDefinitionHandle));
        getTypeMethod = RoslynReflection.GetMethod(Type, "GetType", typeof(CSharpCompilation), InternalModuleId.Type, typeof(int));
    }

    // A compilation referencing every block ('MakeAssemblyReferencesKind.AllAssemblies'), the way the expression
    // compiler builds one when it does not know which module the expression will bind against
    public static CSharpCompilation ToCompilation(object metadataBlocks) {
        var allAssemblies = Enum.Parse(makeAssemblyReferencesKindType, "AllAssemblies");
        return (CSharpCompilation)toCompilationMethod.InvokeUnwrapped(null, [metadataBlocks, InternalModuleId.Default, allAssemblies])!;
    }
    // The user's method behind a frame's method: the kickoff method of a state machine's MoveNext, the method itself otherwise
    public static object? GetSourceMethod(CSharpCompilation compilation, object moduleId, MethodDefinitionHandle methodHandle) {
        return getSourceMethodMethod.InvokeUnwrapped(null, [compilation, moduleId, methodHandle]);
    }
    public static object GetMethod(CSharpCompilation compilation, object moduleId, MethodDefinitionHandle methodHandle) {
        return getMethodMethod.InvokeUnwrapped(null, [compilation, moduleId, methodHandle])!;
    }
    public static object GetType(CSharpCompilation compilation, object moduleId, int typeToken) {
        return getTypeMethod.InvokeUnwrapped(null, [compilation, moduleId, typeToken])!;
    }
}
