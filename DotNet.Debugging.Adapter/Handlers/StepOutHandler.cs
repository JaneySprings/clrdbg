using DotNet.Debugging.Engine.Enums;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override StepOutResponse HandleStepOutRequest(StepOutArguments arguments) {
        return Invoke(() => {
            InvokeDebugger(() => session.StepAsync(arguments.ThreadId, StepKind.Out));
            return new StepOutResponse();
        });
    }
}