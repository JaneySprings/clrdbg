using System.Runtime.InteropServices;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Extensions;
using DotNet.Debugging.Engine.Logging;
using DotNet.Debugging.Engine.Variables;

namespace DotNet.Debugging.Engine.Evaluation;

// Runs code in the debuggee (ICorDebugEval) and waits for its completion. While an evaluation runs the
// debuggee is continued, so every callback that arrives until the EvalComplete one is dispatched as usual
internal class FuncEvalRunner {
    // An evaluation that does not complete in time is aborted: the wait holds the engine's lock, so a getter that
    // blocks (a lock nothing releases, a read that never returns) would otherwise wedge every later request for
    // the rest of the session. Microsoft's debugger cuts evaluations off the same way
    private const int EvalTimeoutMilliseconds = 5000;
    // An abort completes the evaluation once its thread reaches a safe point; a thread that never does is aborted rudely
    private const int AbortTimeoutMilliseconds = 5000;

    private readonly Func<Task<CorDebugManagedCallbackEventArgs>> waitForEvalEvent;

    public bool IsRunning { get; private set; }

    public FuncEvalRunner(Func<Task<CorDebugManagedCallbackEventArgs>> waitForEvalEvent) {
        this.waitForEvalEvent = waitForEvalEvent;
    }

    // 'arguments' must hold the original reference values for instance methods ('this' is not dereferenced)
    public Task<ICorDebugValue?> CallFunctionAsync(ICorDebugEval eval, ICorDebugFunction function, ICorDebugType[] typeArguments, ICorDebugValue[] arguments, bool throwOnException = false) {
        return RunAsync(eval, throwOnException,
            () => eval.CallParameterizedFunction(function, NullIfEmpty(typeArguments), arguments),
            GetFunctionResult);
    }
    public Task<ICorDebugValue?> NewObjectAsync(ICorDebugEval eval, ICorDebugFunction constructor, ICorDebugType[] typeArguments, ICorDebugValue[] arguments, bool throwOnException = false) {
        return RunAsync(eval, throwOnException,
            () => eval.NewParameterizedObject(constructor, NullIfEmpty(typeArguments), arguments),
            it => it.GetResult());
    }
    public Task<ICorDebugValue?> NewObjectNoConstructorAsync(ICorDebugEval eval, ICorDebugClass corClass, ICorDebugType[] typeArguments, bool throwOnException = false) {
        return RunAsync(eval, throwOnException,
            () => eval.NewParameterizedObjectNoConstructor(corClass, NullIfEmpty(typeArguments)),
            it => it.GetResult());
    }
    public Task<ICorDebugValue?> NewArrayAsync(ICorDebugEval eval, ICorDebugType elementType, uint length, bool throwOnException = false) {
        return NewArrayAsync(eval, elementType, [length], throwOnException);
    }
    public Task<ICorDebugValue?> NewArrayAsync(ICorDebugEval eval, ICorDebugType elementType, uint[] dimensions, bool throwOnException = false) {
        return RunAsync(eval, throwOnException,
            () => eval.NewParameterizedArray(elementType, dimensions, new uint[dimensions.Length]),
            it => it.GetResult());
    }
    public async Task<ICorDebugValue> NewStringAsync(ICorDebugEval eval, string text, bool throwOnException = false) {
        var result = await RunAsync(eval, throwOnException, () => eval.NewString(text), it => it.GetResult());
        return result ?? throw new EvaluationException("The string could not be created in the debuggee");
    }

    // Reads a static field, running the type's static constructor when it has not run yet. The frame (needed for
    // thread statics) is obtained through 'getFrame', as the constructor run neuters the one read first
    public async Task<ICorDebugValue> GetStaticFieldValueAsync(ICorDebugType type, FieldDefToken fieldDef, Func<ICorDebugILFrame> getFrame) {
        var frame = getFrame();
        var result = type.TryGetStaticFieldValue(fieldDef, frame, out var value);
        if (result == Cor.CORDBG_E_STATIC_VAR_NOT_AVAILABLE || result == Cor.CORDBG_E_CLASS_NOT_LOADED || result == Cor.E_FAIL) {
            var eval = frame.GetChain().GetThread().CreateEval();
            var instance = await NewObjectNoConstructorAsync(eval, type.GetClass(), type.GetTypeParameters());
            if (instance is ICorDebugHandleValue handle)
                handle.TryDispose();
            result = type.TryGetStaticFieldValue(fieldDef, getFrame(), out value);
        }
        Marshal.ThrowExceptionForHR(result);
        return value;
    }
    // Calls the getter of a property declared on the value's type or one of its base types
    public async Task<ICorDebugValue?> GetPropertyValueAsync(ICorDebugValue value, ICorDebugILFrame frame, string propertyName) {
        var type = value.GetExactType();
        while (type != null) {
            var corClass = type.GetClass();
            var module = corClass.GetModule();
            var metadataImport = module.GetMetaDataInterface<IMetaDataImport>();
            var property = metadataImport.FindProperty(corClass.GetToken(), propertyName);
            if (property == null) {
                type = type.GetBaseType();
                continue;
            }

            var getter = metadataImport.GetPropertyProps(property.Value).pmdGetter;
            if (getter.IsNil)
                return null;

            var isStatic = metadataImport.GetMethodProps(getter).pdwAttr.IsMdStatic();
            var eval = frame.GetChain().GetThread().CreateEval();
            ICorDebugValue[] arguments = isStatic ? [] : [value];
            // The getter is invoked with the arguments of the type declaring it, which is a base type once the walk went up
            return await CallFunctionAsync(eval, module.GetFunctionFromToken(getter), type.GetTypeParameters(), arguments);
        }
        return null;
    }

    private async Task<ICorDebugValue?> RunAsync(ICorDebugEval eval, bool throwOnException, Action start, Func<ICorDebugEval, ICorDebugValue?> getResult) {
        start();
        IsRunning = true;
        try {
            eval.GetThread().GetProcess().Continue(false);
            var evalEvent = await WaitForCompletionAsync(eval);
            if (evalEvent is EvalCompleteCorDebugManagedCallbackEventArgs completeEvent) {
                if (completeEvent.Eval != eval)
                    throw new EvaluationException("The EvalComplete callback does not belong to the running evaluation");
                return getResult(eval);
            }
            if (evalEvent is EvalExceptionCorDebugManagedCallbackEventArgs exceptionEvent) {
                if (exceptionEvent.Eval != eval)
                    throw new EvaluationException("The EvalException callback does not belong to the running evaluation");
                var exceptionValue = eval.GetResult() ?? throw new EvaluationException("The evaluation threw an exception, but its value is not available");
                if (!throwOnException)
                    return exceptionValue;

                try {
                    throw new EvaluationThrewException(TypeNameFormatter.GetTypeName(exceptionValue.GetExactType()));
                }
                finally {
                    if (exceptionValue is ICorDebugHandleValue handle)
                        handle.TryDispose();
                }
            }
            return null;
        }
        finally {
            IsRunning = false;
        }
    }
    // Waits for the completion callback, aborting the evaluation when it takes too long. The wait keeps dispatching
    // the callbacks arriving meanwhile, so it is never abandoned: the abort is requested alongside it, and the
    // completion the abort brings ends it
    private async Task<CorDebugManagedCallbackEventArgs> WaitForCompletionAsync(ICorDebugEval eval) {
        var waitTask = waitForEvalEvent();
        if (await Task.WhenAny(waitTask, Task.Delay(EvalTimeoutMilliseconds)) == waitTask)
            return await waitTask;

        DebuggerLoggingService.LogMessage($"The evaluation did not complete within {EvalTimeoutMilliseconds} ms, aborting it");
        eval.TryAbort();
        if (await Task.WhenAny(waitTask, Task.Delay(AbortTimeoutMilliseconds)) != waitTask) {
            DebuggerLoggingService.LogMessage("The evaluation did not respond to the abort, aborting it rudely");
            if (eval is ICorDebugEval2 eval2)
                eval2.TryRudeAbort();
        }
        await waitTask;
        // Whatever the completion carries (the abort's exception, a late result) is released, the caller gets the timeout
        if (eval.TryGetResult(out var result) == Cor.S_OK && result is ICorDebugHandleValue handle)
            handle.TryDispose();
        throw new EvaluationTimeoutException();
    }
    private static ICorDebugValue? GetFunctionResult(ICorDebugEval eval) {
        var result = eval.TryGetResult(out var value);
        if (result != Cor.CORDBG_S_FUNC_EVAL_HAS_NO_RESULT && value == null)
            Marshal.ThrowExceptionForHR(result);
        return value;
    }
    private static ICorDebugType[]? NullIfEmpty(ICorDebugType[] typeArguments) {
        return typeArguments.Length == 0 ? null : typeArguments;
    }
}
