using System.Reflection;

namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.ExpressionEvaluator.MethodContextReuseConstraints: the method and IL span a compiled
// method context stays valid for, so an expression is not recompiled at every offset of the same scope
internal static class InternalMethodContextReuseConstraints {
    public static Type Type { get; }
    private static readonly ConstructorInfo constructor;
    private static readonly MethodInfo areSatisfiedMethod;

    static InternalMethodContextReuseConstraints() {
        Type = RoslynReflection.GetType(RoslynReflection.ExpressionCompilerAssembly, "Microsoft.CodeAnalysis.ExpressionEvaluator.MethodContextReuseConstraints");
        constructor = RoslynReflection.GetConstructor(Type, 4);
        areSatisfiedMethod = RoslynReflection.GetMethod(Type, "AreSatisfied", InternalModuleId.Type, typeof(int), typeof(int), typeof(int));
    }

    // 'reuseSpan' is the ILSpan read from the method's debug information
    public static object Create(object moduleId, int methodToken, int methodVersion, object reuseSpan) {
        return constructor.InvokeUnwrapped([moduleId, methodToken, methodVersion, reuseSpan]);
    }
    public static bool AreSatisfied(object constraints, object moduleId, int methodToken, int methodVersion, int ilOffset) {
        return (bool)areSatisfiedMethod.InvokeUnwrapped(constraints, [moduleId, methodToken, methodVersion, ilOffset])!;
    }
}
