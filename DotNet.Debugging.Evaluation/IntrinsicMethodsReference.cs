using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotNet.Debugging.Evaluation;

// The debugger intrinsics the expression compiler emits calls to (aliases such as '$exception', synthetic variables),
// declared in a small assembly compiled once and referenced by every compilation. Nothing in it ever runs: the
// engine's interpreter recognizes the calls by name and serves them itself
internal static class IntrinsicMethodsReference {
    public static PortableExecutableReference Instance { get; } = MetadataReference.CreateFromImage(Compile());

    private static ImmutableArray<byte> Compile() {
        const string source = """
            namespace Microsoft.VisualStudio.Debugger.Clr;

            public static class IntrinsicMethods
            {
                public static void CreateVariable(System.Type type, string name, System.Guid customTypeInfoPayloadTypeId, byte[] customTypeInfoPayload) { }
                public static object GetObjectByAlias(string name) => throw null;
                public static ref T GetVariableAddress<T>(string name) => throw null;
                public static System.Exception GetException() => throw null;
            }
            """;
        var compilation = CSharpCompilation.Create(
            "DotNet.Debugging.Intrinsics",
            [SyntaxFactory.ParseSyntaxTree(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
            throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(it => it.GetMessage())));
        return ImmutableArray.Create(stream.ToArray());
    }
}
