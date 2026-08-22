using System.Diagnostics;
using DotNet.Debugging.Common.Interop;

namespace DotNet.Debugging.Common.Apple;

public static class MonoLauncher {
    // https://github.com/xamarin/xamarin-macios/issues/21664
    public static bool UseDeviceCtl { get; set; }

    public static Process TcpTunnel(string serial, IEnumerable<int> ports, IProcessLogger? logger = null) {
        return new ProcessRunner(AppleSdkLocator.GetMLaunchPath(), new ProcessArgumentBuilder()
            .Append(ports, port => $"--tcp-tunnel={port}:{port}")
            .Append($"--devname={serial}")
            .Conditional("--use-device-ctl=false", () => !MonoLauncher.UseDeviceCtl), logger)
            .Start();
    }
    public static void InstallDev(string serial, string bundlePath, IProcessLogger? logger = null) {
        var toolPath = AppleSdkLocator.GetMLaunchPath();
        logger?.OnOutputDataReceived(toolPath);
        new ProcessRunner(toolPath, new ProcessArgumentBuilder()
            .Append("--installdev").AppendQuoted(bundlePath)
            .Append($"--devname={serial}")
            .Append("--install-progress")
            .Conditional("--use-device-ctl=false", () => !MonoLauncher.UseDeviceCtl), logger)
            .WaitForExit();
    }
    public static ProcessRunner LaunchDev(string serial, string bundlePath, Dictionary<string, string> environment, IProcessLogger? logger = null) {
        var tool = AppleSdkLocator.GetMLaunchPath();
        var argumentBuilder = new ProcessArgumentBuilder()
            .Append("--launchdev").AppendQuoted(bundlePath)
            .Append($"--devname={serial}")
            .Append("--wait-for-exit");

        // foreach (var arg in arguments)
        //     argumentBuilder.Append($"--argument={arg}");
        foreach (var env in environment)
            argumentBuilder.Append($"--setenv={env.Key}={env.Value}");

        return new ProcessRunner(tool, argumentBuilder, logger);
    }
    public static ProcessRunner LaunchSim(string serial, string bundlePath, Dictionary<string, string> environment, IProcessLogger? logger = null) {
        var toolPath = AppleSdkLocator.GetMLaunchPath();
        logger?.OnOutputDataReceived(toolPath);
        var argumentBuilder = new ProcessArgumentBuilder()
            .Append("--launchsim").AppendQuoted(bundlePath)
            .Append($"--device=:v2:udid={serial}")
            .Append("-v")
            .Append("--wait-for-exit");

        // foreach (var arg in arguments)
        //     argumentBuilder.Append($"--argument={arg}");
        foreach (var env in environment)
            argumentBuilder.Append($"--setenv={env.Key}={env.Value}");

        return new ProcessRunner(toolPath, argumentBuilder, logger);
    }
}