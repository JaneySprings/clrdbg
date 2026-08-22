using System.Diagnostics;

namespace DotNet.Debugging.Common.Apple;

public static class AppleSdkLocator {
    public static string GetIDeviceDirectory() {
        var ideviceDirectory = Environment.GetEnvironmentVariable("IDEVICE_DIR");
        if (Directory.Exists(ideviceDirectory))
            return ideviceDirectory;

        if (RuntimeInfo.IsLinux)
            return Path.Combine("/usr", "bin"); // There is no 'Microsoft.iOS.Linux.Sdk' workload

        var sdkPath = string.Empty;
        var dotnetPacksPath = Path.Combine(MSBuildLocator.GetRootDirectory(), "packs");
        var sdkPaths = Directory.GetDirectories(dotnetPacksPath, "Microsoft.iOS.Windows.Sdk.net*");

        if (sdkPaths.Length > 0)
            sdkPath = sdkPaths.OrderByDescending(x => Path.GetFileName(x)).First();
        if (string.IsNullOrEmpty(sdkPath))
            sdkPath = Path.Combine(dotnetPacksPath, "Microsoft.iOS.Windows.Sdk");
        if (!Directory.Exists(sdkPath))
            throw new DirectoryNotFoundException("Could not find idevice tool");

        var toolLocations = Directory.GetDirectories(sdkPath);
        if (toolLocations.Length == 0)
            throw new FileNotFoundException("Could not find idevice tool");

        var latestToolDirectory = toolLocations.OrderByDescending(x => Path.GetFileName(x)).First();
        return Path.Combine(latestToolDirectory, "tools", "msbuild", "iOS", "imobiledevice-x64");
    }
    public static bool IsAppleDriverRunning() {
        if (RuntimeInfo.IsMacOS)
            throw new PlatformNotSupportedException();

        if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("USBMUXD_CHECK_BYPASS")))
            return true;

        var processName = RuntimeInfo.IsWindows ? "AppleMobileDeviceProcess" : "usbmuxd";
        var process = Process.GetProcessesByName(processName);
        return process.Length > 0;
    }

    public static string GetMLaunchPath() {
        var mlaunchToolPath = Environment.GetEnvironmentVariable("MLAUNCH_PATH");
        if (File.Exists(mlaunchToolPath))
            return mlaunchToolPath;

        var sdkPath = string.Empty;
        var dotnetPacksPath = Path.Combine(MSBuildLocator.GetRootDirectory(), "packs");
        var sdkPaths = Directory.GetDirectories(dotnetPacksPath, "Microsoft.iOS.Sdk.net*");

        if (sdkPaths.Length > 0)
            sdkPath = sdkPaths.OrderByDescending(x => Path.GetFileName(x)).First();
        if (string.IsNullOrEmpty(sdkPath))
            sdkPath = Path.Combine(dotnetPacksPath, "Microsoft.iOS.Sdk");
        if (!Directory.Exists(sdkPath))
            throw new DirectoryNotFoundException("Could not find mlaunch tool");

        var toolLocations = Directory.GetDirectories(sdkPath);
        if (toolLocations.Length == 0)
            throw new FileNotFoundException("Could not find mlaunch tool");

        var latestToolDirectory = toolLocations.OrderByDescending(x => Path.GetFileName(x)).First();
        mlaunchToolPath = Path.Combine(latestToolDirectory, "tools", "bin", "mlaunch");
        return mlaunchToolPath;
    }
    public static string GetOpenPath() {
        string path = Path.Combine("/usr", "bin", "open");
        if (!File.Exists(path))
            throw new InvalidOperationException("Could not find open tool");

        return path;
    }
}