using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override ContinueResponse HandleContinueRequest(ContinueArguments arguments) {
        return Invoke(() => {
            InvokeDebugger(() => session.HandleContinueRequest());
            return new ContinueResponse() { AllThreadsContinued = true };
        });
    }
}