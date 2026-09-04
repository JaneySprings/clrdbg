using System.Reflection;
using Microsoft.CodeAnalysis;

namespace DotNet.Debugging.Engine.Reflection;

// Microsoft.CodeAnalysis.DiagnosticBag: the pooled bag a compilation reports its diagnostics into
internal static class InternalDiagnosticBag {
    public static Type Type { get; }
    private static readonly MethodInfo getInstanceMethod;
    private static readonly MethodInfo hasAnyErrorsMethod;
    private static readonly MethodInfo asEnumerableMethod;
    private static readonly MethodInfo freeMethod;

    static InternalDiagnosticBag() {
        Type = RoslynReflection.GetType(RoslynReflection.CommonAssembly, "Microsoft.CodeAnalysis.DiagnosticBag");
        getInstanceMethod = RoslynReflection.GetMethod(Type, "GetInstance", Type.EmptyTypes);
        hasAnyErrorsMethod = RoslynReflection.GetMethod(Type, "HasAnyErrors", Type.EmptyTypes);
        asEnumerableMethod = RoslynReflection.GetMethod(Type, "AsEnumerable", Type.EmptyTypes);
        freeMethod = RoslynReflection.GetMethod(Type, "Free", Type.EmptyTypes);
    }

    // Taken from Roslyn's pool, 'Free' returns it
    public static object GetInstance() {
        return getInstanceMethod.Invoke(null, null)!;
    }
    public static bool HasAnyErrors(object diagnostics) {
        return (bool)hasAnyErrorsMethod.Invoke(diagnostics, null)!;
    }
    public static IEnumerable<Diagnostic> AsEnumerable(object diagnostics) {
        return (IEnumerable<Diagnostic>)asEnumerableMethod.Invoke(diagnostics, null)!;
    }
    public static void Free(object diagnostics) {
        freeMethod.Invoke(diagnostics, null);
    }
}
