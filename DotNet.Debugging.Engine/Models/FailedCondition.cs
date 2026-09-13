namespace DotNet.Debugging.Engine.Models;

// A breakpoint whose condition could not be evaluated, reported with the stop it caused
public class FailedCondition {
    public Breakpoint Breakpoint { get; }
    public string Error { get; }

    public FailedCondition(Breakpoint breakpoint, string error) {
        Breakpoint = breakpoint;
        Error = error;
    }
}
