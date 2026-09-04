using System.Reflection;

namespace DotNet.Debugging.Engine.Reflection;

// The internal members of Microsoft.CodeAnalysis.CSharp.Symbol, the base of every C# symbol, the evaluator needs
internal static class InternalSymbol {
    public static Type Type { get; }
    private static readonly PropertyInfo containingModuleProperty;

    static InternalSymbol() {
        Type = RoslynReflection.GetType(RoslynReflection.CSharpAssembly, "Microsoft.CodeAnalysis.CSharp.Symbol");
        containingModuleProperty = RoslynReflection.GetProperty(Type, "ContainingModule");
    }

    // The ModuleSymbol declaring the symbol, a PEModuleSymbol for one read from metadata
    public static object GetContainingModule(object symbol) {
        return containingModuleProperty.GetValue(symbol)!;
    }
}
