namespace DotNet.Debugging.Engine.Models;

public class FunctionBreakpointRequest {
    public string Name { get; }
    public string? Condition { get; set; }
    public string? HitCondition { get; set; }

    public FunctionBreakpointRequest(string name) {
        Name = name;
    }
}
