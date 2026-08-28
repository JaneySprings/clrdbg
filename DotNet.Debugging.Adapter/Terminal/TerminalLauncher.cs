using System.IO.Pipes;
using System.Text.Json;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Models;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter.Terminal;

public class TerminalLauncher : IDisposable {
    private const int ConnectionTimeoutMs = 60000;
    private const int LaunchTimeoutMs = 30000;

    private readonly NamedPipeServerStream pipeServer;
    private readonly CancellationTokenSource abortSource;
    private string? abortReason;
    private bool disposed;

    public string ConnectionPath { get; }

    public TerminalLauncher() {
        var pipeName = $"clrdbg-{Guid.NewGuid():N}";
        ConnectionPath = OperatingSystem.IsWindows() ? pipeName : Path.Combine(Path.GetTempPath(), $"CoreFxPipe_{pipeName}");
        pipeServer = new NamedPipeServerStream(ConnectionPath, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        abortSource = new CancellationTokenSource();
    }

    public RunInTerminalRequest CreateRunInTerminalRequest(ConsoleType console, string title) {
        var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The path of the debugger executable could not be determined");
        var arguments = new List<string>() { executablePath };
        if (Path.GetFileNameWithoutExtension(executablePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            arguments.Add(typeof(TerminalLauncher).Assembly.Location);

        arguments.Add($"{Program.ConnectionOption}{ConnectionPath}");
        var request = new RunInTerminalRequest() {
            Title = title,
            Arguments = arguments,
            Kind = console == ConsoleType.ExternalTerminal ? RunInTerminalArguments.KindValue.External : RunInTerminalArguments.KindValue.Integrated,
            Cwd = console == ConsoleType.ExternalTerminal ? Path.GetDirectoryName(executablePath) : string.Empty,
        };
        // Microsoft's debugger sends an empty environment for an external terminal and none for an integrated one
        if (console == ConsoleType.ExternalTerminal)
            request.Env = new Dictionary<string, object>();
        return request;
    }
    public int LaunchProgram(LaunchInfo launchInfo) {
        WaitForHost();

        var reader = new StreamReader(pipeServer);
        var writer = new StreamWriter(pipeServer) { AutoFlush = true };
        writer.WriteLine(JsonSerializer.Serialize(new TerminalLaunchRequest() {
            Program = launchInfo.Program,
            Arguments = launchInfo.Arguments,
            WorkingDirectory = launchInfo.WorkingDirectory,
            Environment = launchInfo.Environment,
        }));

        var readTask = Task.Run(() => reader.ReadLine());
        if (!readTask.Wait(LaunchTimeoutMs))
            throw new InvalidOperationException(Resources.MsgTerminalLaunchTimeout);

        var response = readTask.Result == null ? null : JsonSerializer.Deserialize<TerminalLaunchResponse>(readTask.Result);
        if (response?.ProcessId == null)
            throw new InvalidOperationException(string.Format(Resources.MsgTerminalLaunchFailed, response?.Error));
        return response.ProcessId.Value;
    }
    public void Abort(string reason) {
        if (disposed)
            return;
        abortReason = reason;
        abortSource.Cancel();
    }

    public void Dispose() {
        if (disposed)
            return;
        disposed = true;
        abortSource.Cancel();
        abortSource.Dispose();
        pipeServer.Dispose();
    }
    private void WaitForHost() {
        if (pipeServer.IsConnected)
            return;
        try {
            using var timeoutSource = new CancellationTokenSource(ConnectionTimeoutMs);
            using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, abortSource.Token);
            pipeServer.WaitForConnectionAsync(waitSource.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) {
            if (abortReason != null)
                throw new InvalidOperationException(string.Format(Resources.MsgTerminalLaunchFailed, abortReason));
            throw new InvalidOperationException(Resources.MsgTerminalLaunchTimeout);
        }
    }
}
