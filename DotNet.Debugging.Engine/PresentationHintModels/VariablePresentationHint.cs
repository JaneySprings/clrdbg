namespace DotNet.Debugging.Engine.PresentationHintModels;

public record struct VariablePresentationHint {
    public PresentationHintKind? Kind { get; set; }
    public AttributesValue? Attributes { get; set; }
}