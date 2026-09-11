using DotNet.Debugging.Adapter.Symbols;
using DotNet.Debugging.Engine.Logging;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override AttachResponse HandleAttachRequest(AttachArguments arguments) {
        return Invoke(() => {
            var configuration = new AttachConfiguration(arguments.ConfigurationProperties);
            configuration.VerifyMissingProperties();

            if (configuration.Logging.LicenseBanner)
                OnDebugDataReceived(Resources.MsgLicenseBanner);
            if (configuration.Logging.EngineLogging)
                DebuggerLoggingService.OnEngineMessage += OnDebugDataReceived;

            sourceLinkResolver = new SourceLinkResolver(configuration.SourceLinkOptions);
            sourceFileMapper = new SourceFileMapper(configuration.SourceFileMap);
            symbolsResolver = new SymbolsResolver(configuration.SymbolOptions);
            debugAgent = configuration.CreateDebugAgent(this);

            InvokeDebugger(() => {
                session.JustMyCode = configuration.JustMyCode;
                session.RequireExactSource = configuration.RequireExactSource;
                session.EnableStepFiltering = configuration.EnableStepFiltering;
            });
            // Breakpoints arrive after this event, the attach itself is made on 'ConfigurationDone'
            Protocol.SendEvent(new InitializedEvent());
            return new AttachResponse();
        });
    }
}