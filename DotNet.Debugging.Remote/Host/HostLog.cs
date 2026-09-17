using System.Diagnostics;

namespace DotNet.Debugging.Remote;

// The host library runs inside the debugger's process but has no logger of its own there; a Debug build writes to a
// file, a Release build writes nothing: the calls are compiled out with their arguments
internal static class HostLog {
    private static readonly object writeLock = new object();
    private static string? path;

    [Conditional("DEBUG")]
    public static void Write(string message) {
        lock (writeLock) {
            try {
                if (path == null)
                    path = Environment.GetEnvironmentVariable("CLRDBG_REMOTE_HOST_LOG") ?? Path.Combine(Path.GetTempPath(), "clrdbg-remote-host.log");
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
            catch {
                // a log that cannot be written is not worth failing the debugger for
            }
        }
    }
}
