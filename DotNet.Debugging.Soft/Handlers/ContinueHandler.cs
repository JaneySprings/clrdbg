using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override ContinueResponse HandleContinueRequest(ContinueArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            InvokeDebugger(() => session.HandleContinueRequest());
            return new ContinueResponse() { AllThreadsContinued = true };
        });
    }
}