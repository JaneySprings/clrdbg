using DotNet.Debugging.Common.Logging;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override TerminateResponse HandleTerminateRequest(TerminateArguments arguments) {
        try {
            if (debugAgent is LaunchDebugAgent or AttachDebugAgent or MobileDebugAgent)
                InvokeDebugger(() => session.Terminate());
        }
        catch (Exception ex) {
            CurrentSessionLogger.Error($"[Handled] Failed to terminate the debuggee {ex}");
        }
        finally {
            debugAgent?.Dispose();
            // The debugger detaches on 'Terminate' and can no longer deliver the exit event itself
            Protocol.SendEvent(new TerminatedEvent());
        }
        return new TerminateResponse();
    }
}