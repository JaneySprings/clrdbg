using DotNet.Debugging.Adapter.Symbols;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Logging;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public partial class DebugSession {
    protected override LaunchResponse HandleLaunchRequest(LaunchArguments arguments) {
        return Invoke(() => {
            var configuration = new LaunchConfiguration(arguments.ConfigurationProperties);
            configuration.VerifyMissingProperties();

            if (configuration.Logging.LicenseBanner)
                OnDebugDataReceived(Resources.MsgLicenseBanner);
            if (configuration.Logging.EngineLogging)
                DebuggerLoggingService.OnEngineMessage += OnDebugDataReceived;

            sourceLinkResolver = new SourceLinkResolver(configuration.SourceLinkOptions);
            sourceFileMapper = new SourceFileMapper(configuration.SourceFileMap);
            symbolsResolver = new SymbolsResolver(configuration.SymbolOptions);
            debugAgent = configuration.CreateDebugAgent(this);

            if (configuration.Console != ConsoleType.InternalConsole && debugAgent is LaunchDebugAgent launchDebugAgent) {
                var request = launchDebugAgent.CreateRunInTerminalRequest(configuration.Console);
                Protocol.SendClientRequest(request, (_, _) => { }, (_, error) => launchDebugAgent.TerminalLauncher?.Abort(error.Message));
            }

            InvokeDebugger(() => {
                session.JustMyCode = configuration.JustMyCode;
                session.RequireExactSource = configuration.RequireExactSource;
                session.EnableStepFiltering = configuration.EnableStepFiltering;
            });
            // Breakpoints arrive after this event, the program itself is started on 'ConfigurationDone'
            Protocol.SendEvent(new InitializedEvent());
            return new LaunchResponse();
        });
    }
}