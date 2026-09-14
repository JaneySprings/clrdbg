using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override ContinueResponse HandleContinueRequest(ContinueArguments arguments) {
        return Invoke(() => {
            InvokeDebugger(() => session.Continue());
            return new ContinueResponse() { AllThreadsContinued = true };
        });
    }
}