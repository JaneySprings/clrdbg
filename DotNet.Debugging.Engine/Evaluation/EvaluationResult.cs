using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Engine.Evaluation;

internal class EvaluationResult : IDisposable {
    private ICorDebugHandleValue? ownedHandle;

    public ICorDebugValue? Value { get; }
    public string? Error { get; }
    // The evaluation was cut off for taking too long, which an implicit one displays as a fallback rather than an error
    public bool TimedOut { get; }

    private EvaluationResult(ICorDebugValue? value, ICorDebugHandleValue? ownedHandle, string? error, bool timedOut) {
        Value = value;
        this.ownedHandle = ownedHandle;
        Error = error;
        TimedOut = timedOut;
    }

    // 'ownedHandle' is the strong handle keeping 'value' alive, released with the result unless the caller keeps it
    public static EvaluationResult FromValue(ICorDebugValue? value, ICorDebugHandleValue? ownedHandle = null) {
        return new EvaluationResult(value, ownedHandle, null, false);
    }
    public static EvaluationResult FromError(string error, bool timedOut = false) {
        return new EvaluationResult(null, null, error, timedOut);
    }

    // The caller takes over the handle, it is then released with the variables references
    public void KeepHandle() {
        ownedHandle = null;
    }

    public void Dispose() {
        ownedHandle?.TryDispose();
        ownedHandle = null;
    }
}
