using DotNet.Debugging.Engine.PresentationHintModels;

namespace DotNet.Debugging.Engine.Models.Response;

public class VariableInfo {
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required string? Type { get; set; }
    public required int VariablesReference { get; set; }
    public VariablePresentationHint? PresentationHint { get; set; }
}