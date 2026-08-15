using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Soft;

public class LaunchDebugAgent : BaseDebugAgent<LaunchConfiguration> {
    public LaunchDebugAgent(LaunchConfiguration configuration) : base(configuration) { }

    public override void PrepareTarget(DebugSession debugSession) {
        // ICorDebug launches the debuggee itself when the debugger connects (see ManagedDebugger.ConfigurationDone)
    }
    public override void Connect(ManagedDebugger debugger) {
        debugger.Launch(Configuration.GetLaunchInfo(), Configuration.JustMyCode);
    }
}