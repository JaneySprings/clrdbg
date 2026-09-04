using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.Debugger.Evaluation;

namespace DotNet.Debugging.Engine.Reflection;

// The Roslyn assemblies the expression evaluator reaches into and the lookups behind every 'Internal*' wrapper of
// this folder. The wrappers resolve their members once, at first use, and a member a Roslyn update moved or renamed
// fails there with its name rather than later with a null - the engine is written against one pinned package version
internal static class RoslynReflection {
    public const BindingFlags AnyMember = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public static readonly Assembly CommonAssembly = typeof(Compilation).Assembly;
    public static readonly Assembly CSharpAssembly = typeof(CSharpCompilation).Assembly;
    // The language neutral expression compiler, reached through one of the public Dkm stubs the package keeps
    public static readonly Assembly ExpressionCompilerAssembly = typeof(DkmEvaluationFlags).Assembly;
    // The C# expression compiler has no public type at all, it is loaded by name
    public static readonly Assembly CSharpExpressionCompilerAssembly = Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.ExpressionCompiler"));

    public static Type GetType(Assembly assembly, string fullName) {
        return assembly.GetType(fullName) ?? throw new MissingMemberException($"The Roslyn type '{fullName}' was not found in {Describe(assembly)}");
    }
    public static ConstructorInfo GetConstructor(Type type, params Type[] parameterTypes) {
        return type.GetConstructor(AnyMember, parameterTypes) ?? throw Missing(type, $".ctor({DescribeParameters(parameterTypes)})");
    }
    // The single constructor with the given number of parameters, for parameter types that cannot be named
    public static ConstructorInfo GetConstructor(Type type, int parameterCount) {
        var constructors = type.GetConstructors(AnyMember).Where(it => it.GetParameters().Length == parameterCount).ToList();
        if (constructors.Count != 1)
            throw Missing(type, $".ctor with {parameterCount} parameters ({constructors.Count} found)");
        return constructors[0];
    }
    public static MethodInfo GetMethod(Type type, string name, params Type[] parameterTypes) {
        return type.GetMethod(name, AnyMember, parameterTypes) ?? throw Missing(type, $"{name}({DescribeParameters(parameterTypes)})");
    }
    // The single overload with the given number of parameters, for parameter types that cannot be named
    public static MethodInfo GetMethod(Type type, string name, int parameterCount) {
        var methods = type.GetMethods(AnyMember).Where(it => it.Name == name && it.GetParameters().Length == parameterCount).ToList();
        if (methods.Count != 1)
            throw Missing(type, $"{name} with {parameterCount} parameters ({methods.Count} found)");
        return methods[0];
    }
    public static FieldInfo GetField(Type type, string name) {
        return type.GetField(name, AnyMember) ?? throw Missing(type, name);
    }
    public static PropertyInfo GetProperty(Type type, string name) {
        return type.GetProperty(name, AnyMember) ?? throw Missing(type, name);
    }

    // ImmutableArray<T> over an element type that cannot be named
    public static Type MakeImmutableArrayType(Type elementType) {
        return typeof(ImmutableArray<>).MakeGenericType(elementType);
    }
    public static object CreateImmutableArray(Type elementType, Array items) {
        var createRange = typeof(ImmutableArray).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(it => it.Name == nameof(ImmutableArray.CreateRange) && it.IsGenericMethodDefinition && it.GetParameters().Length == 1 && it.GetGenericArguments().Length == 1);
        return createRange.MakeGenericMethod(elementType).InvokeUnwrapped(null, [items])!;
    }
    public static object EmptyImmutableArray(Type elementType) {
        return GetField(MakeImmutableArrayType(elementType), nameof(ImmutableArray<int>.Empty)).GetValue(null)!;
    }
    // 'default(ImmutableArray<T>)', the uninitialized array some Roslyn members take for "none"
    public static object DefaultImmutableArray(Type elementType) {
        return Activator.CreateInstance(MakeImmutableArrayType(elementType))!;
    }

    // Invokes without the TargetInvocationException wrapper: what Roslyn throws is the error worth reporting
    public static object? InvokeUnwrapped(this MethodInfo method, object? target, object?[]? arguments) {
        return method.Invoke(target, BindingFlags.DoNotWrapExceptions, null, arguments, null);
    }
    public static object InvokeUnwrapped(this ConstructorInfo constructor, object?[]? arguments) {
        return constructor.Invoke(BindingFlags.DoNotWrapExceptions, null, arguments, null);
    }

    private static MissingMemberException Missing(Type type, string member) {
        return new MissingMemberException($"The Roslyn member '{type.FullName}.{member}' was not found in {Describe(type.Assembly)}");
    }
    private static string Describe(Assembly assembly) {
        var name = assembly.GetName();
        return $"{name.Name} {name.Version}";
    }
    private static string DescribeParameters(Type[] parameterTypes) {
        return string.Join(", ", parameterTypes.Select(it => it.Name));
    }
}
