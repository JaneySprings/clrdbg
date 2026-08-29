using DotNet.Debugging.Adapter.Extensions;
using DotNet.Debugging.Common.Interop;
using DotNet.Debugging.Common.Logging;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public abstract class Session : DebugAdapterBase, IProcessLogger {
    private readonly CurrentClassLogger logger;

    protected Session(Stream input, Stream output) {
        Console.SetError(TextWriter.Null);
        Console.SetOut(TextWriter.Null);
        Console.SetIn(TextReader.Null);

        logger = new CurrentClassLogger(nameof(DebugSession));
        InitializeProtocolClient(input, output);
    }

    public void Start() {
        Protocol.LogMessage += LogMessage;
        Protocol.DispatcherError += LogError;
        Protocol.Run();
    }
    public T Invoke<T>(Func<T> handler) {
        try {
            return handler.Invoke();
        }
        catch (Exception ex) {
            if (ex is ProtocolException)
                throw;
            CurrentSessionLogger.Error($"[Handled] {ex.ToString()}");
            throw Session.GetProtocolException(ex.Message);
        }
    }

    protected abstract void OnEmergencyStopReceived();
    protected abstract bool OnTraceMessageReceived();
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
        if (OnTraceMessageReceived())
            OnDebugDataReceived(args.Message);
    }
    private void LogError(object? sender, DispatcherErrorEventArgs args) {
        logger.Error(args.Exception);
        OnEmergencyStopReceived();
    }

    public static ProtocolException GetProtocolException(string message) {
        return new ProtocolException(message, message.GetHashCode(), message, url: $"file://{LogConfig.DebugLogFile}");
    }
}