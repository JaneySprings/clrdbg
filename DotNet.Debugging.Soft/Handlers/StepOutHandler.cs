using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override StepOutResponse HandleStepOutRequest(StepOutArguments arguments) {
        return Invoke(() => {
            InvokeDebugger(() => session.StepOut(arguments.ThreadId));
            return new StepOutResponse();
        });
    }
}