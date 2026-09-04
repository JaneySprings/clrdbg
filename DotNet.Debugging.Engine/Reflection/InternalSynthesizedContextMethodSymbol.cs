using System.Reflection;

namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.SynthesizedContextMethodSymbol: the method a type context
// (a DebuggerDisplay expression) is compiled in, as if declared on the displayed type
internal static class InternalSynthesizedContextMethodSymbol {
    public static Type Type { get; }
    private static readonly ConstructorInfo constructor;

    static InternalSynthesizedContextMethodSymbol() {
        Type = RoslynReflection.GetType(RoslynReflection.CSharpExpressionCompilerAssembly, "Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.SynthesizedContextMethodSymbol");
        constructor = RoslynReflection.GetConstructor(Type, 1);
    }

    // (NamedTypeSymbol container)
    public static object Create(object containingType) {
        return constructor.InvokeUnwrapped([containingType]);
    }
}
