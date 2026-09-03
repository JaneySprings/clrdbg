using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override ConfigurationDoneResponse HandleConfigurationDoneRequest(ConfigurationDoneArguments arguments) {
        return Invoke(() => {
            ArgumentNullException.ThrowIfNull(debugAgent, nameof(debugAgent));
            // The client has sent its breakpoints, the debuggee can be started now
            InvokeDebugger(() => debugAgent.ConnectAsync(session));
            return new ConfigurationDoneResponse();
        });
    }
}