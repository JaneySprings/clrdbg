using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override LaunchResponse HandleLaunchRequest(LaunchArguments arguments) {
        return Invoke(() => {
            OnDebugDataReceived(Resources.MsgLicenseBanner);

            var configuration = new LaunchConfiguration(arguments.ConfigurationProperties);
            configuration.VerifyMissingProperties();

            ConnectDebugAgent(configuration);
            // Breakpoints arrive after this event and the launch itself is deferred until 'ConfigurationDone'
            Protocol.SendEvent(new InitializedEvent());
            return new LaunchResponse();
        });
    }
}