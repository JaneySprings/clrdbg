using DotNet.Debugging.Common.Interop;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public class SkipDebugAgent : BaseDebugAgent<LaunchConfiguration> {
    public SkipDebugAgent(LaunchConfiguration configuration, DebugSession debugSession) : base(configuration, debugSession) { }

    public override void Connect(ManagedDebugger debugger) {
        var launchInfo = Configuration.GetLaunchInfo();

        var arguments = new ProcessArgumentBuilder();
        foreach (var argument in launchInfo.Arguments)
            arguments.Append(argument);

        var runner = new ProcessRunner(launchInfo.Program, arguments, DebugSession);
        runner.SetWorkingDirectory(launchInfo.Cwd);
        foreach (var kvp in launchInfo.Env)
            runner.SetEnvironmentVariable(kvp.Key, kvp.Value);

        var process = runner.Start();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => {
            DebugSession.Protocol.TrySendEvent(new TerminatedEvent());
        };

        Disposables.Add(() => {
            if (!process.HasExited)
                process.Kill();
        });
    }
}