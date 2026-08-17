using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override LaunchResponse HandleLaunchRequest(LaunchArguments arguments) {
        return Invoke(() => {
            var configuration = new LaunchConfiguration(arguments.ConfigurationProperties);
            configuration.VerifyMissingProperties();

            debugAgent = configuration.CreateDebugAgent(this);
            InvokeDebugger(() => debugAgent.Connect(session));
            // Breakpoints arrive after this event and the launch itself is deferred until 'ConfigurationDone'
            Protocol.SendEvent(new InitializedEvent());
            return new LaunchResponse();
        });
    }
}