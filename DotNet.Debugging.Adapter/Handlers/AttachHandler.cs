using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override AttachResponse HandleAttachRequest(AttachArguments arguments) {
        return Invoke(() => {
            var configuration = new AttachConfiguration(arguments.ConfigurationProperties);
            configuration.VerifyMissingProperties();

            OnDebugDataReceived(Resources.MsgLicenseBanner);
            ConnectDebugAgent(configuration);
            // Breakpoints arrive after this event and the attach itself is deferred until 'ConfigurationDone'
            Protocol.SendEvent(new InitializedEvent());
            return new AttachResponse();
        });
    }
}