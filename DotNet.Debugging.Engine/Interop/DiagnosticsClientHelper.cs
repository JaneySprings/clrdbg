using Microsoft.Diagnostics.NETCore.Client;

namespace DotNet.Debugging.Engine.Interop;

internal static class DiagnosticsClientHelper {
    // Resumes a runtime started with DOTNET_DefaultDiagnosticPortSuspend. Applications that host their own
    // runtime (e.g. godot) open the diagnostics port late, so the connection is retried for a while
    public static async Task ResumeRuntimeAsync(int processId) {
        const int maxAttempts = 5;
        var client = new DiagnosticsClient(processId);
        var delay = 50;
        for (var attempt = 1; attempt <= maxAttempts; attempt++) {
            try {
                await Task.Delay(delay);
                client.ResumeRuntime();
                return;
            }
            catch (ServerNotAvailableException) when (attempt < maxAttempts) {
                delay *= 2;
            }
        }
    }
}
