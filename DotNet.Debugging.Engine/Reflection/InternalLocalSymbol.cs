namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.CSharp.Symbols.LocalSymbol and TypeSymbol: the type arguments of the debug information
// and the element type of the locals handed to an evaluation context
internal static class InternalLocalSymbol {
    public static Type Type { get; }
    public static Type TypeSymbolType { get; }
    // ImmutableArray<LocalSymbol>
    public static Type ImmutableArrayType { get; }

    // 'default(ImmutableArray<LocalSymbol>)', the locals of a type context
    public static object DefaultImmutableArray { get; }

    static InternalLocalSymbol() {
        Type = RoslynReflection.GetType(RoslynReflection.CSharpAssembly, "Microsoft.CodeAnalysis.CSharp.Symbols.LocalSymbol");
        TypeSymbolType = RoslynReflection.GetType(RoslynReflection.CSharpAssembly, "Microsoft.CodeAnalysis.CSharp.Symbols.TypeSymbol");
        ImmutableArrayType = RoslynReflection.MakeImmutableArrayType(Type);
        DefaultImmutableArray = RoslynReflection.DefaultImmutableArray(Type);
    }
}
