using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.VisualStudio.Debugger.Clr;

namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.ExpressionEvaluator.Alias: a pseudo variable an expression may name ('$exception')
internal static class InternalAlias {
    public static Type Type { get; }
    private static readonly ConstructorInfo constructor;

    // An empty ImmutableArray<Alias>
    public static object Empty { get; }

    static InternalAlias() {
        Type = RoslynReflection.GetType(RoslynReflection.ExpressionCompilerAssembly, "Microsoft.CodeAnalysis.ExpressionEvaluator.Alias");
        constructor = RoslynReflection.GetConstructor(Type, typeof(DkmClrAliasKind), typeof(string), typeof(string), typeof(string), typeof(Guid), typeof(ReadOnlyCollection<byte>));
        Empty = RoslynReflection.EmptyImmutableArray(Type);
    }

    public static object Create(DkmClrAliasKind kind, string name, string fullName, string typeName) {
        return constructor.InvokeUnwrapped([kind, name, fullName, typeName, Guid.Empty, null]);
    }
    // An ImmutableArray<Alias> of the given aliases
    public static object CreateArray(IReadOnlyList<object> aliases) {
        var items = Array.CreateInstance(Type, aliases.Count);
        for (var i = 0; i < aliases.Count; i++)
            items.SetValue(aliases[i], i);
        return RoslynReflection.CreateImmutableArray(Type, items);
    }
}
