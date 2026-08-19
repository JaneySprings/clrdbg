using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace DotNet.Debugging.Common;

public static class RuntimeInfo {
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsAarch64 => RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    public static string ExecExtension => IsWindows ? ".exe" : "";
    public static string LibExtension => IsWindows ? ".dll" : IsLinux ? ".so" : ".dylib";
    public static string HomeDirectory => IsWindows
        ? Environment.GetEnvironmentVariable("USERPROFILE")!
        : Environment.GetEnvironmentVariable("HOME")!;
    public static string ProgramX86Directory => IsWindows
        ? Environment.GetEnvironmentVariable("ProgramFiles(x86)")!
        : throw new PlatformNotSupportedException();

    public static int GetFreePort() {
        TcpListener? listener = null;
        try {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally {
            listener?.Stop();
        }
    }

    public static string GetArchitecture() {
        return IsAarch64 ? "arm64" : "x64";
    }
    public static string GetOperationSystem() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "win";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "osx";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "linux";
        return "unknown";
    }
}