using DotNet.Debugging.Engine.Logging;
using Microsoft.Diagnostics.NETCore.Client;

namespace DotNet.Debugging.Engine.Interop;

internal static class DiagnosticsClientHelper {
    // Resumes a runtime started with DOTNET_DefaultDiagnosticPortSuspend
    public static Task ResumeRuntimeAsync(int processId) {
        return SendAsync(processId, client => client.ResumeRuntime());
    }
    // Resumes a debuggee the debugger parked itself, after taking the DOTNET_DefaultDiagnosticPortSuspend it put
    // there back out of the debuggee's environment. The runtime read the variable at startup and is parked on it
    // already: what is rewritten is the environment the debuggee passes on. An environment is inherited, so every
    // process the debuggee starts would otherwise park on its own diagnostics port, with no debugger to resume
    // it, and hang the debuggee the moment it waits for that child. 'configuredValue' is what the launch
    // configuration set the variable to, if it did: that value is put back, otherwise the variable is removed.
    // The runtime applies the change to its own copy of the environment - the one it serves managed code from,
    // so a child started through Process.Start gets the corrected value; on Unix the libc environment a native
    // spawn inherits keeps the debugger's value
    public static Task ResumeLaunchedRuntimeAsync(int processId, string? configuredValue) {
        return SendAsync(processId, client => {
            RestoreSuspendVariable(client, processId, configuredValue);
            client.ResumeRuntime();
        });
    }

    private static void RestoreSuspendVariable(DiagnosticsClient client, int processId, string? configuredValue) {
        try {
            client.SetEnvironmentVariable(ManagedDebugger.DiagnosticPortSuspendVariable, configuredValue);
        }
        catch (Exception ex) when (ex is not ServerNotAvailableException) {
            // The debuggee itself runs fine without the change, only its child processes would not start: the launch goes on
            DebuggerLoggingService.LogMessage(
                $"'{ManagedDebugger.DiagnosticPortSuspendVariable}' could not be taken out of the environment of process {processId}, its child processes may hang at startup: {ex.Message}");
        }
    }
    // Applications that host their own runtime (e.g. godot) open the diagnostics port late, so the connection is retried for a while
    private static async Task SendAsync(int processId, Action<DiagnosticsClient> send) {
        const int maxAttempts = 5;
        var client = new DiagnosticsClient(processId);
        var delay = 50;
        for (var attempt = 1; attempt <= maxAttempts; attempt++) {
            try {
                await Task.Delay(delay);
                send.Invoke(client);
                return;
            }
            catch (ServerNotAvailableException) when (attempt < maxAttempts) {
                delay *= 2;
            }
        }
    }
}
