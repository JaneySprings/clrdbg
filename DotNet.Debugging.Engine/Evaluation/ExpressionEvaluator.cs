using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Logging;

namespace DotNet.Debugging.Engine.Evaluation;

// Evaluates C# expressions in the context of a frame (or of a value, for DebuggerDisplay) by compiling them
// with Roslyn and interpreting the resulting CIL against the debuggee
internal class ExpressionEvaluator {
    private readonly ManagedDebugger debugger;
    private readonly ExpressionCompiler compiler;
    private readonly CilInterpreter interpreter;

    public ExpressionEvaluator(ManagedDebugger debugger, PrimitiveTypeClasses primitiveTypes) {
        this.debugger = debugger;
        compiler = new ExpressionCompiler(debugger);
        interpreter = new CilInterpreter(debugger, primitiveTypes);
    }

    public async Task<EvaluationResult> EvaluateAsync(string expression, EvaluationContext context) {
        try {
            if (expression == "$exception") {
                var currentException = debugger.GetCurrentException(context.ThreadId);
                if (currentException != null)
                    return CreateExceptionResult(currentException);
            }
            var compiled = compiler.Compile(expression, context);
            return await interpreter.InterpretAsync(compiled, context);
        }
        catch (Exception ex) {
            // The client gets the message alone, the log keeps the interpreter's stack for a failure that needs a look
            DebuggerLoggingService.LogError($"Evaluation of '{expression}' failed", ex);
            return EvaluationResult.FromError($"error: {ex.Message}");
        }
    }

    private static EvaluationResult CreateExceptionResult(ICorDebugValue currentException) {
        if (currentException is not ICorDebugReferenceValue reference || reference.IsNull())
            return EvaluationResult.FromValue(currentException);
        using var handles = new EvaluationHandleScope();
        var handle = handles.CreateHandle(reference);
        return EvaluationResult.FromValue(handle, handles.Detach(handle));
    }
}
