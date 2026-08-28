using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override EvaluateResponse HandleEvaluateRequest(EvaluateArguments arguments) {
        return Invoke(() => {
            // Console input typed while the debuggee runs is routed into its standard input
            if (arguments.Context == EvaluateArguments.ContextValue.Repl && InvokeDebugger(() => session.IsRunning && session.WriteStandardInput(arguments.Expression))) {
                return new EvaluateResponse(string.Empty, 0) {
                    PresentationHint = new VariablePresentationHint() {
                        Attributes = VariablePresentationHint.AttributesValue.ReadOnly | VariablePresentationHint.AttributesValue.FailedEvaluation,
                    },
                };
            }

            var expression = arguments.Expression?.TrimEnd(';');
            if (string.IsNullOrEmpty(expression))
                throw new ProtocolException("Expression is empty");
            if (arguments.FrameId == null || arguments.FrameId == 0)
                throw new ProtocolException("Frame ID is required for evaluation");

            var variable = InvokeDebugger(() => session.EvaluateAsync(expression, arguments.FrameId.Value));
            return new EvaluateResponse() {
                Result = variable.Value,
                Type = variable.Type,
                VariablesReference = variable.VariablesReference
            };
        });
    }
}