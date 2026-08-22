using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Engine.Evaluation;

internal class EvaluationResult : IDisposable {
    private ICorDebugHandleValue? ownedHandle;

    public ICorDebugValue? Value { get; }
    public string? Error { get; }

    private EvaluationResult(ICorDebugValue? value, ICorDebugHandleValue? ownedHandle, string? error) {
        Value = value;
        this.ownedHandle = ownedHandle;
        Error = error;
    }

    // 'ownedHandle' is the strong handle keeping 'value' alive, released with the result unless the caller keeps it
    public static EvaluationResult FromValue(ICorDebugValue? value, ICorDebugHandleValue? ownedHandle = null) {
        return new EvaluationResult(value, ownedHandle, null);
    }
    public static EvaluationResult FromError(string error) {
        return new EvaluationResult(null, null, error);
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
