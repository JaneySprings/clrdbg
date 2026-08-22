using System.Runtime.InteropServices;

namespace DotNet.Debugging.Engine.Interop;

// Reads the OS-level thread names of another process (set via pthread_setname_np / SetThreadDescription), e.g. '.NET TP Worker'
internal static class NativeThreadNames {
    private const int PROC_PIDTHREADID64INFO = 15;
    // struct proc_threadinfo: 2x uint64 + 8x int32, then char pth_name[64]
    private const int ProcThreadInfoSize = 112;
    private const int ProcThreadInfoNameOffset = 48;
    private const int ProcThreadInfoNameLength = 64;
    private const uint THREAD_QUERY_LIMITED_INFORMATION = 0x0800;

    public static string? GetThreadName(int processId, int threadId) {
        string? name = null;
        try {
            if (OperatingSystem.IsMacOS())
                name = GetMacOSThreadName(processId, threadId);
            else if (OperatingSystem.IsLinux())
                name = GetLinuxThreadName(processId, threadId);
            else if (OperatingSystem.IsWindows())
                name = GetWindowsThreadName(threadId);
        }
        catch {
            // Native thread names are an optional nicety
        }
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private static string? GetMacOSThreadName(int processId, int threadId) {
        var buffer = Marshal.AllocHGlobal(ProcThreadInfoSize);
        try {
            // ICorDebugThread.GetId is the unique thread id reported by pthread_threadid_np
            var size = proc_pidinfo(processId, PROC_PIDTHREADID64INFO, (uint)threadId, buffer, ProcThreadInfoSize);
            if (size < ProcThreadInfoNameOffset + ProcThreadInfoNameLength)
                return null;
            return Marshal.PtrToStringUTF8(buffer + ProcThreadInfoNameOffset);
        }
        finally {
            Marshal.FreeHGlobal(buffer);
        }
    }
    private static string? GetLinuxThreadName(int processId, int threadId) {
        var commPath = $"/proc/{processId}/task/{threadId}/comm";
        return File.Exists(commPath) ? File.ReadAllText(commPath).Trim() : null;
    }
    private static string? GetWindowsThreadName(int threadId) {
        var threadHandle = OpenThread(THREAD_QUERY_LIMITED_INFORMATION, false, (uint)threadId);
        if (threadHandle == IntPtr.Zero)
            return null;
        try {
            if (GetThreadDescription(threadHandle, out var description) < 0 || description == IntPtr.Zero)
                return null;
            try {
                return Marshal.PtrToStringUni(description);
            }
            finally {
                LocalFree(description);
            }
        }
        finally {
            CloseHandle(threadHandle);
        }
    }

    [DllImport("libproc", SetLastError = true)]
    private static extern int proc_pidinfo(int pid, int flavor, ulong arg, IntPtr buffer, int bufferSize);
    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);
    [DllImport("kernel32", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32", SetLastError = true)]
    private static extern int GetThreadDescription(IntPtr threadHandle, out IntPtr description);
    [DllImport("kernel32")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
