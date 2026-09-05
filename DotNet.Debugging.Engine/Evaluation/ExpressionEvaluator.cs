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
            // An expression may need an assembly the debuggee has not loaded (System.Linq, for a program that never
            // used it): the assembly is loaded there and the expression compiled again, once per assembly
            var loadedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (true) {
                CompiledExpression compiled;
                try {
                    compiled = compiler.Compile(expression, context);
                }
                catch (MissingAssembliesException ex) when (ex.AssemblyNames.Any(it => !loadedAssemblies.Contains(it))) {
                    if (!await TryLoadAssembliesAsync(ex.AssemblyNames, loadedAssemblies, context))
                        throw;
                    continue;
                }
                return await interpreter.InterpretAsync(compiled, context);
            }
        }
        catch (Exception ex) {
            // The client gets the message alone, the log keeps the interpreter's stack for a failure that needs a look
            DebuggerLoggingService.LogError($"Evaluation of '{expression}' failed", ex);
            // A Roslyn wrapper of 'Reflection/' that found a member missing fails in its type initializer, the cause is the message worth showing
            var cause = ex is TypeInitializationException { InnerException: { } initializationFailure } ? initializationFailure : ex;
            return EvaluationResult.FromError($"error: {cause.Message}", ex is EvaluationTimeoutException);
        }
    }

    // Deliberate divergence: Microsoft's debugger reads a missing assembly's metadata from disk and interprets its IL
    // itself, clrdbg loads the assembly into the debuggee (a module event follows), as the Results View does. False
    // when an assembly could not be loaded, the compiler's error stands then
    private async Task<bool> TryLoadAssembliesAsync(IReadOnlyList<string> assemblyNames, HashSet<string> loadedAssemblies, EvaluationContext context) {
        foreach (var assemblyName in assemblyNames) {
            if (!loadedAssemblies.Add(assemblyName))
                continue;
            using var result = await EvaluateAsync($"System.Reflection.Assembly.Load(\"{assemblyName}\")", context);
            if (result.Error != null) {
                DebuggerLoggingService.LogMessage($"The expression needs '{assemblyName}', which could not be loaded into the debuggee: {result.Error}");
                return false;
            }
        }
        return true;
    }

    private static EvaluationResult CreateExceptionResult(ICorDebugValue currentException) {
        if (currentException is not ICorDebugReferenceValue reference || reference.IsNull())
            return EvaluationResult.FromValue(currentException);
        using var handles = new EvaluationHandleScope();
        var handle = handles.CreateHandle(reference);
        return EvaluationResult.FromValue(handle, handles.Detach(handle));
    }
}
