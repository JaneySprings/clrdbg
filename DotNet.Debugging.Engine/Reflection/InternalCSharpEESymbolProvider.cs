using System.Reflection;

namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.CSharpEESymbolProvider: resolves the types and locals named in
// a method's debug information to symbols of the compilation
internal static class InternalCSharpEESymbolProvider {
    public static Type Type { get; }
    private static readonly ConstructorInfo constructor;

    static InternalCSharpEESymbolProvider() {
        Type = RoslynReflection.GetType(RoslynReflection.CSharpExpressionCompilerAssembly, "Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.CSharpEESymbolProvider");
        constructor = RoslynReflection.GetConstructor(Type, 3);
    }

    // (SourceAssemblySymbol sourceAssembly, PEModuleSymbol module, PEMethodSymbol method)
    public static object Create(object sourceAssembly, object peModule, object peMethod) {
        return constructor.Invoke([sourceAssembly, peModule, peMethod]);
    }
}
