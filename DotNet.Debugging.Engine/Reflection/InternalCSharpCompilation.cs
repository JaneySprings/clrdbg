using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;

namespace DotNet.Debugging.Engine.Reflection;

// The internal members of Microsoft.CodeAnalysis.CSharp.CSharpCompilation the evaluator needs
internal static class InternalCSharpCompilation {
    private static readonly PropertyInfo sourceAssemblyProperty;

    static InternalCSharpCompilation() {
        sourceAssemblyProperty = RoslynReflection.GetProperty(typeof(CSharpCompilation), "SourceAssembly");
    }

    // The SourceAssemblySymbol of the compilation
    public static object GetSourceAssembly(CSharpCompilation compilation) {
        return sourceAssemblyProperty.GetValue(compilation)!;
    }
}
