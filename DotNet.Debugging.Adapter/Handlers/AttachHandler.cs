using DotNet.Debugging.Adapter.Symbols;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override AttachResponse HandleAttachRequest(AttachArguments arguments) {
        return Invoke(() => {
            var configuration = new AttachConfiguration(arguments.ConfigurationProperties);
            configuration.VerifyMissingProperties();

            sourceLinkResolver = new SourceLinkResolver(configuration.SourceLinkOptions);
            debugAgent = configuration.CreateDebugAgent(this);

            OnDebugDataReceived(Resources.MsgLicenseBanner);
            InvokeDebugger(() => {
                session.JustMyCode = configuration.JustMyCode;
                debugAgent.Connect(session);
            });
            // Breakpoints arrive after this event and the attach itself is deferred until 'ConfigurationDone'
            Protocol.SendEvent(new InitializedEvent());
            return new AttachResponse();
        });
    }
}