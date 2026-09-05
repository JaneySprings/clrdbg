using System;
using System.Collections.Generic;

namespace DotNet.Debugging.Evaluation;

// What compiling an expression produced: the in-memory assembly and the method in it to run, or the errors instead
public class ExpressionCompileResult {
    public byte[]? Assembly { get; }
    public string? TypeName { get; }
    public string? MethodName { get; }
    public IReadOnlyList<string> Errors { get; }

    public ExpressionCompileResult(byte[] assembly, string typeName, string methodName) {
        Assembly = assembly;
        TypeName = typeName;
        MethodName = methodName;
        Errors = Array.Empty<string>();
    }
    public ExpressionCompileResult(IReadOnlyList<string> errors) {
        Errors = errors;
    }
}
