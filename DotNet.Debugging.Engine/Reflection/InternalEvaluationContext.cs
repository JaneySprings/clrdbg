using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.Debugger.Evaluation;

namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.EvaluationContext: the C# expression compiler proper, bound to a
// method (a frame's method, its locals and the hoisted locals in scope) or to a type (a DebuggerDisplay expression)
internal static class InternalEvaluationContext {
    public static Type Type { get; }
    private static readonly ConstructorInfo constructor;
    private static readonly MethodInfo compileExpressionMethod;
    private static readonly FieldInfo methodContextReuseConstraintsField;

    static InternalEvaluationContext() {
        Type = RoslynReflection.GetType(RoslynReflection.CSharpExpressionCompilerAssembly, "Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.EvaluationContext");
        constructor = RoslynReflection.GetConstructor(Type, 7);
        compileExpressionMethod = RoslynReflection.GetMethod(Type, "CompileExpression", 6);
        methodContextReuseConstraintsField = RoslynReflection.GetField(Type, "MethodContextReuseConstraints");
    }

    // (MethodContextReuseConstraints? constraints, CSharpCompilation compilation, MethodSymbol currentFrame,
    // MethodSymbol? currentSourceMethod, ImmutableArray<LocalSymbol> locals, ImmutableSortedSet<int> inScopeHoistedLocalSlots,
    // MethodDebugInfo<TypeSymbol, LocalSymbol> methodDebugInfo); the constraints are null for a type context
    public static object Create(object? reuseConstraints, CSharpCompilation compilation, object currentFrame, object? currentSourceMethod, object locals, ImmutableSortedSet<int> inScopeHoistedLocalSlots, object methodDebugInfo) {
        return constructor.Invoke([reuseConstraints, compilation, currentFrame, currentSourceMethod, locals, inScopeHoistedLocalSlots, methodDebugInfo]);
    }
    // The CompileResult, null when the expression did not compile - the diagnostics say why. 'aliases' is an
    // ImmutableArray<Alias>; the result properties and the test data are of no use here
    public static object? CompileExpression(object context, string expression, DkmEvaluationFlags flags, object aliases, object diagnostics) {
        return compileExpressionMethod.Invoke(context, [expression, flags, aliases, diagnostics, null, null]);
    }
    // The constraints a method context was created with, null for a type context
    public static object? GetMethodContextReuseConstraints(object context) {
        return methodContextReuseConstraintsField.GetValue(context);
    }
}
