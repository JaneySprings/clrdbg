using System.Diagnostics;

namespace DotNet.Debugging.Common;

public static class ProcessExtensions {
    // private const int ExitTimeout = 1000;

    public static void Terminate(this Process process, bool entireProcessTree = false) {
        if (!process.HasExited) {
            process.Kill(entireProcessTree);
            // process.WaitForExit(ExitTimeout);
        }
        process.Close();
    }
    public static void AddFinalizer(this Process process, Action finalizer) {
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => finalizer.Invoke();
    }
}
