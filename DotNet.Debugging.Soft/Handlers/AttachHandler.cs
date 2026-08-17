using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public partial class DebugSession {
    protected override AttachResponse HandleAttachRequest(AttachArguments arguments) {
        return Invoke(() => {
            var configuration = new AttachConfiguration(arguments.ConfigurationProperties);
            configuration.VerifyMissingProperties();

            debugAgent = configuration.CreateDebugAgent(this);
            InvokeDebugger(() => debugAgent.Connect(session));
            // Breakpoints arrive after this event and the attach itself is deferred until 'ConfigurationDone'
            Protocol.SendEvent(new InitializedEvent());
            return new AttachResponse();
        });
    }
}