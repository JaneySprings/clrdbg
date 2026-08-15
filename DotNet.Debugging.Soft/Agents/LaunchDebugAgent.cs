using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Soft;

public class LaunchDebugAgent : BaseDebugAgent<LaunchConfiguration> {
    public LaunchDebugAgent(LaunchConfiguration configuration, DebugSession debugSession) : base(configuration, debugSession) { }

    public override void Connect(ManagedDebugger debugger) {
        debugger.Launch(Configuration.GetLaunchInfo(), Configuration.JustMyCode);
    }
}