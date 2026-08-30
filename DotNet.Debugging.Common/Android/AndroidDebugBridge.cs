using System.Diagnostics;
using DotNet.Debugging.Common.Interop;

namespace DotNet.Debugging.Common.Android;

public static class AndroidDebugBridge {
    public static string Shell(string serial, params string[] args) {
        var adb = AndroidSdkLocator.GetAdbPath();
        var result = new ProcessRunner(adb, new ProcessArgumentBuilder()
            .Append("-s", serial)
            .Append("shell")
            .Append(args))
            .WaitForExit();

        if (!result.Success)
            return string.Empty;

        return result.GetOutput().Trim();
    }
    public static ProcessResult ShellResult(string serial, params string[] args) {
        var adb = AndroidSdkLocator.GetAdbPath();
        var result = new ProcessRunner(adb, new ProcessArgumentBuilder()
            .Append("-s", serial)
            .Append("shell")
            .Append(args))
            .WaitForExit();

        return result;
    }

    public static string Forward(string serial, int port) {
        var adb = AndroidSdkLocator.GetAdbPath();
        var result = new ProcessRunner(adb, new ProcessArgumentBuilder()
            .Append("-s", serial)
            .Append("forward")
            .Append($"tcp:{port}")
            .Append($"tcp:{port}"))
            .WaitForExit();

        return string.Join(Environment.NewLine, result.StandardOutput);
    }
    public static string RemoveForward(string serial) {
        var adb = AndroidSdkLocator.GetAdbPath();
        var result = new ProcessRunner(adb, new ProcessArgumentBuilder()
            .Append("-s", serial)
            .Append("forward")
            .Append("--remove-all"))
            .WaitForExit();

        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.StandardError));

        return string.Join(Environment.NewLine, result.StandardOutput);
    }
    public static List<string> GetDevices() {
        var adb = AndroidSdkLocator.GetAdbPath();
        ProcessResult result = new ProcessRunner(adb, new ProcessArgumentBuilder()
            .Append("devices")
            .Append("-l"))
            .WaitForExit(5000);

        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.StandardError));

        var devices = new List<string>();
        foreach (var line in result.StandardOutput) {
            if (line.StartsWith("list of", StringComparison.OrdinalIgnoreCase))
                continue;

            var data = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (data.Length > 1 && data[1].Equals("device", StringComparison.OrdinalIgnoreCase))
                devices.Add(data[0]);
        }

        return devices;
    }

    public static void Install(string serial, string apk, IProcessLogger? logger = null) {
        var adb = AndroidSdkLocator.GetAdbPath();
        var arguments = new ProcessArgumentBuilder()
            .Append("-s", serial)
            .Append("install")
            .AppendQuoted(apk);

        var result = new ProcessRunner(adb, arguments, logger).WaitForExit();
        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.StandardError));
    }
    public static void Uninstall(string serial, string pkg, IProcessLogger? logger = null) {
        var adb = AndroidSdkLocator.GetAdbPath();
        var argument = new ProcessArgumentBuilder()
            .Append("-s", serial)
            .Append("uninstall")
            .Append(pkg);
        _ = new ProcessRunner(adb, argument, logger).WaitForExit();
    }
    public static void Launch(string serial, string pkg, IProcessLogger? logger = null) {
        // This is a legacy method that is no longer used (device auto-rotation issue).
        // string result = Shell(serial, "monkey", "--pct-syskeys", "0", "-p", pkg, "1");
        var result = ShellResult(serial, "am", "start", $"{pkg}/$(cmd package resolve-activity -c android.intent.category.LAUNCHER {pkg} | sed -n '/name=/s/^.*name=//p')");
        logger?.OnOutputDataReceived(result.GetAllOutput());
    }
    public static void Push(string serial, string source, string destination, IProcessLogger? logger = null) {
        var adb = AndroidSdkLocator.GetAdbPath();
        var arguments = new ProcessArgumentBuilder()
            .Append("-s", serial)
            .Append("push")
            .AppendQuoted(source)
            .AppendQuoted(destination);
        var result = new ProcessRunner(adb, arguments, logger).WaitForExit();
        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.StandardError));
    }

    public static void Flush(string serial) {
        var adb = AndroidSdkLocator.GetAdbPath();
        _ = new ProcessRunner(adb, new ProcessArgumentBuilder()
            .Append("-s", serial)
            .Append("logcat")
            .Append("-c"))
            .WaitForExit();
    }
    public static Process Logcat(string serial, IProcessLogger logger) {
        var adb = AndroidSdkLocator.GetAdbPath();
        var arguments = new ProcessArgumentBuilder()
            .Append("-s", serial)
            .Append("logcat")
            .Append("-v", "tag");
        return new ProcessRunner(adb, arguments, logger).Start();
    }
    public static Process Logcat(string serial, string applicationId, IProcessLogger logger) {
        string applicationProcessId = Shell(serial, "pidof", "-s", applicationId);

        // If we can't get the current app PID, return full logcat
        if (string.IsNullOrEmpty(applicationProcessId))
            return Logcat(serial, logger);

        var adb = AndroidSdkLocator.GetAdbPath();
        var arguments = new ProcessArgumentBuilder()
            .Append("-s", serial)
            .Append("logcat")
            .Append("--pid", applicationProcessId)
            .Append("-v", "tag");
        return new ProcessRunner(adb, arguments, logger).Start();
    }

    public static bool StartServer() {
        ProcessResult result = new ProcessRunner(AndroidSdkLocator.GetAdbPath(), new ProcessArgumentBuilder()
            .Append("start-server"))
            .WaitForExit();

        return result.Success;
    }
}