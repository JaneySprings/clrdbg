using DotNet.Debugging.Engine.PresentationHintModels;
using DotNet.Debugging.Adapter.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override EvaluateResponse HandleEvaluateRequest(EvaluateArguments arguments) {
        return Invoke(() => {
            var expression = arguments.Expression?.TrimEnd(';');
            if (string.IsNullOrEmpty(expression))
                throw new ProtocolException("Expression is empty");

            var variable = InvokeDebugger(() => session.Evaluate(expression, arguments.FrameId));
            if (variable.PresentationHint?.Attributes == AttributesValue.FailedEvaluation)
                throw new ProtocolException(variable.Value);

            return new EvaluateResponse() {
                Result = variable.Value,
                Type = variable.Type,
                VariablesReference = variable.VariablesReference
            };
        });
    }
}