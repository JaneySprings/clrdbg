using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Models;

namespace DotNet.Debugging.Soft;

public class DebugLaunchAgent : BaseLaunchAgent {
    private readonly LaunchInfo launchInfo;

    public DebugLaunchAgent(LaunchConfiguration configuration) : base(configuration) {
        launchInfo = configuration.GetLaunchInfo();
    }
    public override void Launch(DebugSession debugSession) {
        // ICorDebug launches the debuggee itself when the debugger connects (see ManagedDebugger.ConfigurationDone)
    }
    public override void Connect(ManagedDebugger debugger) {
        debugger.Launch(launchInfo, Configuration.JustMyCode);
    }
}
