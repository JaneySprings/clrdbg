namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.ExpressionEvaluator.ModuleId: the identity (MVID and display name) the expression compiler knows a module by
internal static class InternalModuleId {
    public static Type Type { get; }
    private static readonly System.Reflection.ConstructorInfo constructor;

    // 'default(ModuleId)', what a compilation over every block is created with
    public static object Default { get; }

    static InternalModuleId() {
        Type = RoslynReflection.GetType(RoslynReflection.ExpressionCompilerAssembly, "Microsoft.CodeAnalysis.ExpressionEvaluator.ModuleId");
        constructor = RoslynReflection.GetConstructor(Type, typeof(Guid), typeof(string));
        Default = Activator.CreateInstance(Type)!;
    }

    public static object Create(Guid mvid, string displayName) {
        return constructor.InvokeUnwrapped([mvid, displayName]);
    }
}
