using DotNet.Debugging.Common.Logging;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override DisconnectResponse HandleDisconnectRequest(DisconnectArguments arguments) {
        try {
            if (debugAgent is LaunchDebugAgent or AttachDebugAgent or MobileDebugAgent) {
                // Per the protocol, a launched debuggee is terminated by default while an attached one is left running
                var terminateDebuggee = arguments.TerminateDebuggee ?? debugAgent is LaunchDebugAgent or MobileDebugAgent;
                InvokeDebugger(() => session.Disconnect(terminateDebuggee));
            }
        }
        catch (Exception ex) {
            CurrentSessionLogger.Error($"[Handled] Failed to disconnect from the debuggee {ex}");
        }
        finally {
            debugAgent?.Dispose();
        }
        return new DisconnectResponse();
    }
}