using System.Diagnostics;
using DotNet.Debugging.Common.Apple;
using DotNet.Debugging.Common.Interop;

namespace DotNet.Debugging.Common.Mobile;

/// <summary>
/// Launches a CoreCLR mobile app (maccatalyst or iOS simulator) wired up for remote debugging: it stages the
/// on-target profiler into the bundle and starts the app with the CORECLR_* environment that makes the runtime
/// load that profiler and connect back to the debugger's remote transport on <paramref name="debuggerPort"/>.
///
/// This must be called only after the debugger side is already listening on the port, so the app's connection
/// attempt succeeds.
/// </summary>
public static class CoreClrMobileLauncher {
    public static Process Launch(CoreClrMobileTarget target, int debuggerPort, IProcessLogger? logger = null) {
        StageProfiler(target);
        var environment = BuildEnvironment(target, debuggerPort);

        return target.IsMacCatalyst
            ? LaunchMacCatalyst(target, environment, logger)
            : LaunchSimulator(target, environment, logger);
    }

    /// <summary>The CORECLR_* environment that turns a normal launch into a remote-debuggable one.</summary>
    public static Dictionary<string, string> BuildEnvironment(CoreClrMobileTarget target, int debuggerPort) {
        return new Dictionary<string, string> {
            ["CORECLR_ENABLE_PROFILING"] = "1",
            ["CORECLR_PROFILER"] = VsdbgRemoteResources.ProfilerGuid,
            ["CORECLR_PROFILER_PATH"] = target.ProfilerDestinationPath,
            ["CORECLR_REMOTE_DEBUGGER_IP"] = "127.0.0.1",
            ["CORECLR_REMOTE_DEBUGGER_PORT"] = debuggerPort.ToString(),
            // The debugger is the server (listens); the on-device runtime is the client and connects to it.
            ["CORECLR_REMOTE_DEBUGGER_ISSERVER"] = "0",
            ["DOTNET_MODIFIABLE_ASSEMBLIES"] = "debug"
        };
    }

    /// <summary>Copies the profiler shipped with the MAUI tooling into the bundle so the sandboxed app can dlopen it.</summary>
    private static void StageProfiler(CoreClrMobileTarget target) {
        var destinationDirectory = Path.GetDirectoryName(target.ProfilerDestinationPath)!;
        Directory.CreateDirectory(destinationDirectory);
        target.ProfilerSource.CopyTo(target.ProfilerDestinationPath, overwrite: true);
    }

    private static Process LaunchMacCatalyst(CoreClrMobileTarget target, Dictionary<string, string> environment, IProcessLogger? logger) {
        // maccatalyst apps run on the host, so we launch the bundle's Mach-O executable directly. This gives us a
        // real child process (for lifetime tracking and stdout/stderr forwarding) and lets us inject the env directly.
        var runner = new ProcessRunner(new FileInfo(target.BundleExecutablePath), builder: null, logger);
        foreach (var (key, value) in environment)
            runner.SetEnvironmentVariable(key, value);
        return runner.Start();
    }

    private static Process LaunchSimulator(CoreClrMobileTarget target, Dictionary<string, string> environment, IProcessLogger? logger) {
        if (string.IsNullOrWhiteSpace(target.DeviceUdid))
            throw new InvalidOperationException("A simulator UDID (launch configuration 'device') is required to launch on the iOS simulator.");

        var mlaunch = AppleSdkLocator.MLaunchTool();
        var builder = new ProcessArgumentBuilder()
            .Append("--launchsim").AppendQuoted(target.AppBundlePath)
            .Append($"--device=:v2:udid={target.DeviceUdid}")
            .Append("-v");
        foreach (var (key, value) in environment)
            builder.Append($"--setenv={key}={value}");
        // Keep mlaunch attached to the app so its lifetime tracks the debuggee and its console is forwarded.
        builder.Append("--wait-for-exit:true");

        return new ProcessRunner(mlaunch, builder, logger).Start();
    }
}
