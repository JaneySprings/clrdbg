using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override StepInResponse HandleStepInRequest(StepInArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            InvokeDebugger(() => session.StepIn(arguments.ThreadId));
            return new StepInResponse();
        });
    }
}