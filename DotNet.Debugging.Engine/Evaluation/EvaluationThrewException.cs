using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Engine.Evaluation;

// The evaluated code threw; the exception's type is kept for the callers wording their own message, and the exception
// object itself when the runtime handed it out - the catcher owns it (it is a handle to release)
public class EvaluationThrewException : EvaluationException {
    public string ExceptionTypeName { get; }
    public ICorDebugValue? ExceptionValue { get; }

    public EvaluationThrewException(string exceptionTypeName, ICorDebugValue? exceptionValue = null) : base($"Evaluation threw {exceptionTypeName}") {
        ExceptionTypeName = exceptionTypeName;
        ExceptionValue = exceptionValue;
    }
}
