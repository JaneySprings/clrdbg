using System.Diagnostics;
using DotNet.Debugging.Common.Extensions;
using DotNet.Debugging.Engine;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public class AttachDebugAgent : BaseDebugAgent<AttachConfiguration> {
    public AttachDebugAgent(AttachConfiguration configuration, DebugSession debugSession) : base(configuration, debugSession) { }

    public override void Connect(ManagedDebugger debugger) {
        // ICorDebug does not report the exit of a process it did not launch itself - watch the process explicitly
        var processId = Configuration.GetProcessId();
        var watchdog = new System.Timers.Timer(1000);
        watchdog.Elapsed += (_, _) => {
            if (IsProcessAlive(processId))
                return;

            watchdog.Stop();
            Protocol.SendEvent(new TerminatedEvent());
        };
        watchdog.Start();
        Disposables.Add(() => watchdog.Dispose());

        debugger.Attach(processId);
    }

    private static bool IsProcessAlive(int processId) {
        return SafeExtensions.Invoke(false, () => {
            var process = Process.GetProcessById(processId);
            return !process.HasExited;
        });
    }
}