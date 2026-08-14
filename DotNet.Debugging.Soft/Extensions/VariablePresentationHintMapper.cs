using DotNet.Debugging.CorApi.PresentationHintModels;
using DebugProtocol = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft.Extensions;

public static class VariablePresentationHintMapper {
    public static DebugProtocol.VariablePresentationHint ToDto(this VariablePresentationHint hint) {
        return new DebugProtocol.VariablePresentationHint {
            Kind = hint.Kind?.ToDto(),
            Attributes = hint.Attributes?.ToDto(),
            Visibility = null
        };
    }

    private static DebugProtocol.VariablePresentationHint.KindValue ToDto(this PresentationHintKind kind) {
        return kind switch {
            PresentationHintKind.Property => DebugProtocol.VariablePresentationHint.KindValue.Property,
            PresentationHintKind.Method => DebugProtocol.VariablePresentationHint.KindValue.Method,
            PresentationHintKind.Event => DebugProtocol.VariablePresentationHint.KindValue.Event,
            PresentationHintKind.Class => DebugProtocol.VariablePresentationHint.KindValue.Class,
            PresentationHintKind.Data => DebugProtocol.VariablePresentationHint.KindValue.Data,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }
    private static DebugProtocol.VariablePresentationHint.AttributesValue ToDto(this AttributesValue attributes) {
        return attributes switch {
            AttributesValue.FailedEvaluation => DebugProtocol.VariablePresentationHint.AttributesValue.FailedEvaluation,
            _ => throw new ArgumentOutOfRangeException(nameof(attributes), attributes, null)
        };
    }
}