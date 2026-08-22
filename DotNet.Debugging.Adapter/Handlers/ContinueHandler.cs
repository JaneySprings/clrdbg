using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override ContinueResponse HandleContinueRequest(ContinueArguments arguments) {
        return Invoke(() => {
            InvokeDebugger(() => {
                session.Continue();
                Protocol.SendEvent(new ContinuedEvent(arguments.ThreadId) { AllThreadsContinued = true });
            });
            return new ContinueResponse() { AllThreadsContinued = true };
        });
    }
}