using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override PauseResponse HandlePauseRequest(PauseArguments arguments) {
        return ServerExtensions.DoSafe(() => {
            InvokeDebugger(() => session.Pause());
            return new PauseResponse();
        });
    }
}