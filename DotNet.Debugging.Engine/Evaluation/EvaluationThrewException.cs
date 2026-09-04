namespace DotNet.Debugging.Engine.Evaluation;

// The evaluated code threw; the exception's type is kept for the callers wording their own message
public class EvaluationThrewException : EvaluationException {
    public string ExceptionTypeName { get; }

    public EvaluationThrewException(string exceptionTypeName) : base($"Evaluation threw {exceptionTypeName}") {
        ExceptionTypeName = exceptionTypeName;
    }
}
