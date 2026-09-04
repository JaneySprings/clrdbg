using System.Reflection;

namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.PooledObjects.ArrayBuilder<LocalSymbol>: the pooled builder the locals of a method context are collected in
internal static class InternalArrayBuilder {
    public static Type Type { get; }
    private static readonly MethodInfo getInstanceMethod;
    private static readonly MethodInfo addRangeMethod;
    private static readonly MethodInfo toImmutableAndFreeMethod;

    static InternalArrayBuilder() {
        var openType = RoslynReflection.GetType(RoslynReflection.CommonAssembly, "Microsoft.CodeAnalysis.PooledObjects.ArrayBuilder`1");
        Type = openType.MakeGenericType(InternalLocalSymbol.Type);
        getInstanceMethod = RoslynReflection.GetMethod(Type, "GetInstance", System.Type.EmptyTypes);
        addRangeMethod = RoslynReflection.GetMethod(Type, "AddRange", InternalLocalSymbol.ImmutableArrayType);
        toImmutableAndFreeMethod = RoslynReflection.GetMethod(Type, "ToImmutableAndFree", System.Type.EmptyTypes);
    }

    // Taken from Roslyn's pool, 'ToImmutableAndFree' returns it
    public static object GetInstance() {
        return getInstanceMethod.InvokeUnwrapped(null, null)!;
    }
    public static void AddRange(object builder, object immutableArray) {
        addRangeMethod.InvokeUnwrapped(builder, [immutableArray]);
    }
    // The ImmutableArray<LocalSymbol> built
    public static object ToImmutableAndFree(object builder) {
        return toImmutableAndFreeMethod.InvokeUnwrapped(builder, null)!;
    }
}
