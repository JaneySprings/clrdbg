using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override PauseResponse HandlePauseRequest(PauseArguments arguments) {
        return Invoke(() => {
            InvokeDebugger(() => session.Pause());
            return new PauseResponse();
        });
    }
}