using System.Reflection;

namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.ExpressionEvaluator.EvaluationContextBase: the language neutral base of the evaluation contexts
internal static class InternalEvaluationContextBase {
    public static Type Type { get; }
    private static readonly MethodInfo normalizeILOffsetMethod;

    static InternalEvaluationContextBase() {
        Type = RoslynReflection.GetType(RoslynReflection.ExpressionCompilerAssembly, "Microsoft.CodeAnalysis.ExpressionEvaluator.EvaluationContextBase");
        normalizeILOffsetMethod = RoslynReflection.GetMethod(Type, "NormalizeILOffset", typeof(uint));
    }

    // The special offsets of a prolog or epilog (0xffffffff and friends) are mapped to offset 0
    public static int NormalizeILOffset(uint ilOffset) {
        return (int)normalizeILOffsetMethod.Invoke(null, [ilOffset])!;
    }
}
