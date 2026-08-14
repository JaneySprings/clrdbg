namespace DotNet.Debugging.CorApi.PresentationHintModels;

public record struct VariablePresentationHint {
    public PresentationHintKind? Kind { get; set; }
    public AttributesValue? Attributes { get; set; }
}