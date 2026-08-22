namespace DotNet.Debugging.Engine.Models;

public class BreakpointRequest {
    public int Line { get; }
    public int? Column { get; set; }
    public string? Condition { get; set; }
    public string? HitCondition { get; set; }
    public string? LogMessage { get; set; }

    public BreakpointRequest(int line) {
        Line = line;
    }
}
