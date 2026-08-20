using DotNet.Debugging.Adapter.Extensions;
using DotNet.Debugging.Common;
using DotNet.Debugging.Common.Interop;
using DotNet.Debugging.Engine;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public class SkipDebugAgent : BaseDebugAgent<LaunchConfiguration> {
    public SkipDebugAgent(LaunchConfiguration configuration, DebugSession debugSession) : base(configuration, debugSession) { }

    public override void Connect(ManagedDebugger debugger) {
        var launchInfo = Configuration.GetLaunchInfo();

        var arguments = new ProcessArgumentBuilder();
        foreach (var argument in launchInfo.Arguments)
            arguments.Append(argument);

        var runner = new ProcessRunner(launchInfo.Program, arguments, ProcessLogger);
        runner.SetWorkingDirectory(launchInfo.Cwd);
        foreach (var kvp in launchInfo.Env)
            runner.SetEnvironmentVariable(kvp.Key, kvp.Value);

        var process = runner.Start();

        // Nothing else reports this one: a skipDebug run has no debugger to raise the engine event.
        // Program is non-null: GetLaunchInfo above throws otherwise.
        Protocol.TrySendEvent(new ProcessEvent(Configuration.Program!) {
            SystemProcessId = process.Id,
            StartMethod = ProcessEvent.StartMethodValue.Launch,
            IsLocalProcess = true,
        });

        process.AddFinalizer(() => Protocol.TrySendEvent(new TerminatedEvent()));
        Disposables.Add(() => process.Terminate());
    }
}