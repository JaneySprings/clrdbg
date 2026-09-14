using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;

namespace DotNet.Debugging.Engine.Evaluation;

internal class EvaluationResult : IDisposable {
    private ICorDebugHandleValue? ownedHandle;
    // The exception the evaluated code threw in the debuggee, when the runtime handed it out; released with the result
    private ICorDebugValue? thrownException;

    public ICorDebugValue? Value { get; }
    public string? Error { get; }
    // What failed the evaluation, for a caller telling the failures apart (a timeout, code that threw)
    public Exception? Failure { get; }

    private EvaluationResult(ICorDebugValue? value, ICorDebugHandleValue? ownedHandle, string? error, Exception? failure) {
        Value = value;
        this.ownedHandle = ownedHandle;
        Error = error;
        Failure = failure;
        thrownException = (failure as EvaluationThrewException)?.ExceptionValue;
    }

    // 'ownedHandle' is the strong handle keeping 'value' alive, released with the result unless the caller keeps it
    public static EvaluationResult FromValue(ICorDebugValue? value, ICorDebugHandleValue? ownedHandle = null) {
        return new EvaluationResult(value, ownedHandle, null, null);
    }
    public static EvaluationResult FromError(string error, Exception failure) {
        return new EvaluationResult(null, null, error, failure);
    }

    // The caller takes over the handle, it is then released with the variables references
    public void KeepHandle() {
        ownedHandle = null;
    }

    public void Dispose() {
        ownedHandle?.TryDispose();
        ownedHandle = null;
        if (thrownException is ICorDebugHandleValue handle)
            handle.TryDispose();
        thrownException = null;
    }
}
