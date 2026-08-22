using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override ConfigurationDoneResponse HandleConfigurationDoneRequest(ConfigurationDoneArguments arguments) {
        return Invoke(() => {
            InvokeDebugger(() => session.ConfigurationDoneAsync());
            return new ConfigurationDoneResponse();
        });
    }
}