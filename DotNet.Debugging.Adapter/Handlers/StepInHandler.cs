using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override StepInResponse HandleStepInRequest(StepInArguments arguments) {
        return Invoke(() => {
            InvokeDebugger(() => {
                session.StepIn(arguments.ThreadId);
                Protocol.SendEvent(new ContinuedEvent(arguments.ThreadId) { AllThreadsContinued = true });
            });
            return new StepInResponse();
        });
    }
}