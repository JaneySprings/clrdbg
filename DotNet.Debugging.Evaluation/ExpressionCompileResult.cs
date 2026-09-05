using System;
using System.Collections.Generic;

namespace DotNet.Debugging.Evaluation;

// What compiling an expression produced: the in-memory assembly and the method in it to run, or the errors instead
public class ExpressionCompileResult {
    public byte[]? Assembly { get; }
    public string? TypeName { get; }
    public string? MethodName { get; }
    public IReadOnlyList<string> Errors { get; }
    // The assemblies (simple names) the errors blame for a missing type or extension method: the expression may
    // compile once the debuggee has them, the way Roslyn's own compiler retries with more metadata
    public IReadOnlyList<string> MissingAssemblies { get; }

    public ExpressionCompileResult(byte[] assembly, string typeName, string methodName) {
        Assembly = assembly;
        TypeName = typeName;
        MethodName = methodName;
        Errors = Array.Empty<string>();
        MissingAssemblies = Array.Empty<string>();
    }
    public ExpressionCompileResult(IReadOnlyList<string> errors, IReadOnlyList<string> missingAssemblies) {
        Errors = errors;
        MissingAssemblies = missingAssemblies;
    }
}
