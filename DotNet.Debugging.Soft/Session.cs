using DotNet.Debugging.Common.Interop;
using DotNet.Debugging.Common.Logging;
using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public abstract class Session : DebugAdapterBase, IProcessLogger {
    private readonly CurrentClassLogger logger;

    protected Session(Stream input, Stream output) {
        logger = new CurrentClassLogger(nameof(DebugSession));
        InitializeProtocolClient(input, output);
    }

    protected abstract void OnUnhandledException(Exception ex);

    public void Start() {
        Protocol.LogMessage += LogMessage;
        Protocol.DispatcherError += LogError;
        Protocol.Run();
    }
    public void OnOutputDataReceived(string stdout) {
        SendMessageEvent(OutputEvent.CategoryValue.Stdout, stdout);
    }
    public void OnErrorDataReceived(string stderr) {
        SendMessageEvent(OutputEvent.CategoryValue.Stderr, stderr);
    }
    public void OnDebugDataReceived(string debug) {
        SendMessageEvent(OutputEvent.CategoryValue.Console, debug);
    }
    public void OnImportantDataReceived(string message) {
        SendMessageEvent(OutputEvent.CategoryValue.Important, message);
    }

    private void SendMessageEvent(OutputEvent.CategoryValue category, string message) {
        Protocol.TrySendEvent(new OutputEvent(message.Trim() + Environment.NewLine) { Category = category });
    }
    private void LogMessage(object? sender, LogEventArgs args) {
        logger.Debug(args.Message);
    }
    private void LogError(object? sender, DispatcherErrorEventArgs args) {
        logger.Error(args.Exception);
        OnUnhandledException(args.Exception);
    }
}
