using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Logging;

namespace DotNet.Debugging.Engine.Extensions;

internal static class CorDebugThreadExtensions {
    // The frames of the managed chains, innermost first
    public static IEnumerable<ICorDebugFrame> GetManagedFrames(this ICorDebugThread thread) {
        foreach (var chain in thread.GetChains()) {
            if (!chain.IsManaged())
                continue;
            foreach (var frame in chain.GetFrames())
                yield return frame;
        }
    }
    // The number of managed frames on the thread: tells a frame deeper than another apart, on the same thread
    public static int GetFrameDepth(this ICorDebugThread thread) {
        return thread.GetManagedFrames().Count();
    }
    // Whether the thread runs managed code. The frames cannot be walked while the process runs (an attach that has
    // just landed, a request racing a continue) - nothing tells the thread apart then, and it counts as running some
    public static bool HasManagedFrames(this ICorDebugThread thread) {
        try {
            return thread.GetManagedFrames().Any();
        }
        catch {
            return true;
        }
    }
    // The managed 'Thread.Name': the '_name' field of the Thread object, read without running code
    public static string? GetManagedName(this ICorDebugThread thread) {
        try {
            if (thread.GetObject()?.UnwrapDebugValue() is ICorDebugObjectValue threadObject) {
                var nameValue = threadObject.FindFieldValue("_name");
                if (nameValue?.UnwrapDebugValue() is ICorDebugStringValue name) {
                    var managedName = name.GetString();
                    if (!string.IsNullOrEmpty(managedName))
                        return managedName;
                }
            }
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogMessage($"Failed to read the managed name of thread {thread.GetId()}: {ex.Message}");
        }
        return null;
    }
}
