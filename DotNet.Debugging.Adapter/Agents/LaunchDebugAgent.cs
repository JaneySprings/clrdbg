using DotNet.Debugging.Engine;

namespace DotNet.Debugging.Adapter;

public class LaunchDebugAgent : BaseDebugAgent<LaunchConfiguration> {
    public LaunchDebugAgent(LaunchConfiguration configuration, DebugSession debugSession) : base(configuration, debugSession) { }

    public override void Connect(ManagedDebugger debugger) {
        debugger.Launch(Configuration.GetLaunchInfo(), Configuration.JustMyCode);
    }
}