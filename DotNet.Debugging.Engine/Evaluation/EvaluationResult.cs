using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;

namespace DotNet.Debugging.Engine.Evaluation;

internal class EvaluationResult : IDisposable {
    private ICorDebugHandleValue? ownedHandle;

    public ICorDebugValue? Value { get; }
    public string? Error { get; }
    // The evaluation was cut off for taking too long, which an implicit one displays as a fallback rather than an error
    public bool TimedOut { get; }
    // What failed the evaluation, for a caller telling the failures apart (a null dereference, code that threw)
    public Exception? Failure { get; }
    // The exception the evaluated code threw in the debuggee, when the runtime handed it out; released with the result
    public ICorDebugValue? ThrownException { get; private set; }

    private EvaluationResult(ICorDebugValue? value, ICorDebugHandleValue? ownedHandle, string? error, bool timedOut, Exception? failure) {
        Value = value;
        this.ownedHandle = ownedHandle;
        Error = error;
        TimedOut = timedOut;
        Failure = failure;
        ThrownException = (failure as EvaluationThrewException)?.ExceptionValue;
    }

    // 'ownedHandle' is the strong handle keeping 'value' alive, released with the result unless the caller keeps it
    public static EvaluationResult FromValue(ICorDebugValue? value, ICorDebugHandleValue? ownedHandle = null) {
        return new EvaluationResult(value, ownedHandle, null, false, null);
    }
    public static EvaluationResult FromError(string error, Exception failure, bool timedOut) {
        return new EvaluationResult(null, null, error, timedOut, failure);
    }

    // The caller takes over the handle, it is then released with the variables references
    public void KeepHandle() {
        ownedHandle = null;
    }

    public void Dispose() {
        ownedHandle?.TryDispose();
        ownedHandle = null;
        if (ThrownException is ICorDebugHandleValue handle)
            handle.TryDispose();
        ThrownException = null;
    }
}
