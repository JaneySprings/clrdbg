using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override ConfigurationDoneResponse HandleConfigurationDoneRequest(ConfigurationDoneArguments arguments) {
        return Invoke(() => {
            InvokeDebugger(() => session.ConfigurationDone());
            return new ConfigurationDoneResponse();
        });
    }
}