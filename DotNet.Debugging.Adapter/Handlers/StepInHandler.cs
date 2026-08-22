using DotNet.Debugging.Engine.Enums;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override StepInResponse HandleStepInRequest(StepInArguments arguments) {
        return Invoke(() => {
            InvokeDebugger(async () => {
                await session.StepAsync(arguments.ThreadId, StepKind.Into);
                Protocol.SendEvent(new ContinuedEvent(arguments.ThreadId) { AllThreadsContinued = true });
            });
            return new StepInResponse();
        });
    }
}