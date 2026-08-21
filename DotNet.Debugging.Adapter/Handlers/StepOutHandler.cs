using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override StepOutResponse HandleStepOutRequest(StepOutArguments arguments) {
        return Invoke(() => {
            InvokeDebugger(() => {
                session.StepOut(arguments.ThreadId);
                Protocol.SendEvent(new ContinuedEvent(arguments.ThreadId) { AllThreadsContinued = true });
            });
            return new StepOutResponse();
        });
    }
}