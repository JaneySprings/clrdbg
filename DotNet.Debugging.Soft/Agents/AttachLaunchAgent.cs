using DotNet.Debugging.CorApi;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public class AttachLaunchAgent : BaseLaunchAgent {
    public AttachLaunchAgent(LaunchConfiguration configuration) : base(configuration) { }

    public override void Launch(DebugSession debugSession) {
        // ICorDebug does not report the exit of a process it did not launch itself - watch the process explicitly
        var watchdog = new System.Timers.Timer(1000);
        watchdog.Elapsed += (_, _) => {
            if (IsProcessAlive(Configuration.ProcessId))
                return;

            watchdog.Stop();
            debugSession.Protocol.SendEvent(new TerminatedEvent());
        };
        watchdog.Start();
        Disposables.Add(() => watchdog.Dispose());
    }
    public override void Connect(ManagedDebugger debugger) {
        debugger.Attach(Configuration.ProcessId, Configuration.JustMyCode);
    }

    private static bool IsProcessAlive(int processId) {
        try {
            var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch {
            return false;
        }
    }
}
