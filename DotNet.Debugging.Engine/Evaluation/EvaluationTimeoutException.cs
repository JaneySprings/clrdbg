namespace DotNet.Debugging.Engine.Evaluation;

// The evaluation was aborted for not completing in time
public class EvaluationTimeoutException : EvaluationException {
    public EvaluationTimeoutException() : base("Evaluation timed out") { }
}
