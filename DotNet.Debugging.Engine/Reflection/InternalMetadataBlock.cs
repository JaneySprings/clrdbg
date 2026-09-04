using System.Reflection;

namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.ExpressionEvaluator.MetadataBlock: a module's raw metadata, addressed in memory, for the expression compiler to bind against
internal static class InternalMetadataBlock {
    public static Type Type { get; }
    private static readonly ConstructorInfo constructor;

    static InternalMetadataBlock() {
        Type = RoslynReflection.GetType(RoslynReflection.ExpressionCompilerAssembly, "Microsoft.CodeAnalysis.ExpressionEvaluator.MetadataBlock");
        constructor = RoslynReflection.GetConstructor(Type, InternalModuleId.Type, typeof(Guid), typeof(IntPtr), typeof(int));
    }

    public static object Create(object moduleId, Guid generationId, nint pointer, int size) {
        return constructor.InvokeUnwrapped([moduleId, generationId, (IntPtr)pointer, size]);
    }
    // An ImmutableArray<MetadataBlock> of the given blocks
    public static object CreateArray(IReadOnlyList<object> blocks) {
        var items = Array.CreateInstance(Type, blocks.Count);
        for (var i = 0; i < blocks.Count; i++)
            items.SetValue(blocks[i], i);
        return RoslynReflection.CreateImmutableArray(Type, items);
    }
}
