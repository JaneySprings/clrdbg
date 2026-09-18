using System.Diagnostics;
using System.Runtime.Versioning;

namespace DotNet.Debugging.Engine.Extensions;

internal static class ProcessExtensions {
    // The thread the process began with, the one that runs Main: no other thread of it can be older
    [SupportedOSPlatform("windows")]
    public static int? GetOldestThreadId(this Process process) {
        ProcessThread? oldest = null;
        foreach (ProcessThread thread in process.Threads) {
            if (oldest == null || thread.StartTime < oldest.StartTime)
                oldest = thread;
        }
        return oldest?.Id;
    }
}
