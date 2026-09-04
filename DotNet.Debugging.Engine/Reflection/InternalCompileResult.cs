using System.Reflection;

namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.ExpressionEvaluator.CompileResult: the assembly a compiled expression was emitted into and the method to run
internal static class InternalCompileResult {
    public static Type Type { get; }
    private static readonly FieldInfo assemblyField;
    private static readonly FieldInfo typeNameField;
    private static readonly FieldInfo methodNameField;

    static InternalCompileResult() {
        Type = RoslynReflection.GetType(RoslynReflection.ExpressionCompilerAssembly, "Microsoft.CodeAnalysis.ExpressionEvaluator.CompileResult");
        assemblyField = RoslynReflection.GetField(Type, "Assembly");
        typeNameField = RoslynReflection.GetField(Type, "TypeName");
        methodNameField = RoslynReflection.GetField(Type, "MethodName");
    }

    public static byte[] GetAssembly(object result) {
        return (byte[])assemblyField.GetValue(result)!;
    }
    public static string GetTypeName(object result) {
        return (string)typeNameField.GetValue(result)!;
    }
    public static string GetMethodName(object result) {
        return (string)methodNameField.GetValue(result)!;
    }
}
