using DotNet.Debugging.Adapter.Terminal;
using DotNet.Debugging.Engine;
using DotNet.Debugging.Engine.Enums;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public class LaunchDebugAgent : BaseDebugAgent<LaunchConfiguration> {
    public TerminalLauncher? TerminalLauncher { get; private set; }

    public LaunchDebugAgent(LaunchConfiguration configuration, DebugSession debugSession) : base(configuration, debugSession) { }

    public override void Connect(ManagedDebugger debugger) {
        debugger.Launch(Configuration.GetLaunchRequest());
    }

    public RunInTerminalRequest CreateRunInTerminalRequest(ConsoleType console) {
        if (TerminalLauncher != null)
            throw new InvalidOperationException("Already in use");

        TerminalLauncher = new TerminalLauncher();
        Disposables.Add(() => TerminalLauncher?.Dispose());
        return TerminalLauncher.CreateRunInTerminalRequest(console, Configuration.GetApplicationName());
    }
}