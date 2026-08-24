using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using DotNet.Debugging.Common.Extensions;
using DotNet.Debugging.Engine;

namespace DotNet.Debugging.Adapter.Terminal;

public static class TerminalHost {
    private const int ConnectionTimeoutMs = 10000;

    public static int Run(string connectionPath) {
        return SafeExtensions.Invoke(1, () => {
            using var pipe = new NamedPipeClientStream(".", connectionPath, PipeDirection.InOut);
            pipe.Connect(ConnectionTimeoutMs);

            var reader = new StreamReader(pipe);
            var writer = new StreamWriter(pipe) { AutoFlush = true };
            var requestLine = reader.ReadLine();
            var request = requestLine == null ? null : JsonSerializer.Deserialize<TerminalLaunchRequest>(requestLine);
            if (request == null || string.IsNullOrEmpty(request.Program))
                return 1;

            try {
                var process = StartProcess(request);
                writer.WriteLine(JsonSerializer.Serialize(new TerminalLaunchResponse() { ProcessId = process.Id }));
                process.WaitForExit();
                return process.ExitCode;
            }
            catch (Exception ex) {
                writer.WriteLine(JsonSerializer.Serialize(new TerminalLaunchResponse() { Error = ex.Message }));
                return 1;
            }
        });
    }

    private static Process StartProcess(TerminalLaunchRequest request) {
        var startInfo = new ProcessStartInfo() {
            FileName = request.Program,
            WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
        };

        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);

        startInfo.Environment[ManagedDebugger.DiagnosticPortSuspendVariable] = "1";
        foreach (var (key, value) in request.Environment)
            startInfo.Environment[key] = value;

        return Process.Start(startInfo) ?? throw new InvalidOperationException("The process could not be started");
    }
}
