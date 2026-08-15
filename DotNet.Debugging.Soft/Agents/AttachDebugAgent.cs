using DotNet.Debugging.CorApi;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public class AttachDebugAgent : BaseDebugAgent<AttachConfiguration> {
    public AttachDebugAgent(AttachConfiguration configuration, DebugSession debugSession) : base(configuration, debugSession) { }

    public override void Connect(ManagedDebugger debugger) {
        // ICorDebug does not report the exit of a process it did not launch itself - watch the process explicitly
        var watchdog = new System.Timers.Timer(1000);
        watchdog.Elapsed += (_, _) => {
            if (IsProcessAlive(Configuration.ProcessId))
                return;

            watchdog.Stop();
            DebugSession.Protocol.SendEvent(new TerminatedEvent());
        };
        watchdog.Start();
        Disposables.Add(() => watchdog.Dispose());

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