using DotNet.Debugging.Common;
using DotNet.Debugging.Common.Android;
using DotNet.Debugging.Common.Apple;
using DotNet.Debugging.Common.Extensions;
using DotNet.Debugging.Common.Interop;
using DotNet.Debugging.Engine;
using DotNet.Debugging.Engine.Models;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter;

public class MobileDebugAgent : BaseDebugAgent<LaunchConfiguration> {
    public MobileDebugAgent(LaunchConfiguration configuration, DebugSession debugSession) : base(configuration, debugSession) { }

    public override void Connect(ManagedDebugger debugger) {
        ArgumentNullException.ThrowIfNull(Configuration.MobileOptions);
        debugger.AttachRemote(GetAttachInfo(), onListenerReady: () => {
            Logger.Debug($"Debugger listening on {Configuration.MobileOptions.Address}:{Configuration.MobileOptions.Port}");

            Configuration.EnvironmentVariables.Add("CORECLR_ENABLE_PROFILING", "1");
            Configuration.EnvironmentVariables.Add("CORECLR_PROFILER", "{9DC623E8-C88F-4FD5-AD99-77E67E1D9631}");
            Configuration.EnvironmentVariables.Add("CORECLR_REMOTE_DEBUGGER_IP", Configuration.MobileOptions.Address!);
            Configuration.EnvironmentVariables.Add("CORECLR_REMOTE_DEBUGGER_PORT", Configuration.MobileOptions.Port.ToString());
            Configuration.EnvironmentVariables.Add("CORECLR_REMOTE_DEBUGGER_ISSERVER", Configuration.MobileOptions.IsServer ? "0" : "1");
            Configuration.EnvironmentVariables.Add("DOTNET_MODIFIABLE_ASSEMBLIES", "debug");

            switch (Configuration.MobileOptions.Platform) {
                case DebugTarget.Android:
                    LaunchAndroid();
                    break;
                case DebugTarget.IOS:
                    LaunchAppleMobile();
                    break;
                case DebugTarget.Maccatalyst:
                    LaunchMacCatalyst();
                    break;
                case DebugTarget.CoreClr:
                    throw new NotSupportedException();
            }
        });
    }

    private void LaunchMacCatalyst() {
        var libraryName = "libvsdbgremotecoreclrtarget.dylib";
        var libraryPath = Path.Combine(Configuration.Program, "Contents", "MonoBundle", libraryName);
        if (!File.Exists(libraryPath))
            throw new FileNotFoundException($"File not found: {libraryPath}");

        Configuration.EnvironmentVariables.Add("CORECLR_PROFILER_PATH", libraryName);

        var appProcess = new ProcessRunner(AppleSdkLocator.GetOpenPath(), new ProcessArgumentBuilder()
            .Append("-n", "-W")
            .Append(Configuration.EnvironmentVariables, (kvp) => $"--env \"{kvp.Key}={kvp.Value}\"")
            .AppendQuoted(Configuration.Program), ProcessLogger)
            .Start();

        appProcess.AddFinalizer(() => Protocol.SendEvent(new TerminatedEvent()));
        Disposables.Add(() => SafeExtensions.Invoke(() => appProcess.Terminate(entireProcessTree: true)));
    }
    private void LaunchAppleMobile() {
        var libraryName = "libvsdbgremotecoreclrtarget.dylib";
        var libraryPath = Path.Combine(Configuration.Program, libraryName);
        if (!File.Exists(libraryPath))
            throw new FileNotFoundException($"File not found: {libraryPath}");

        Configuration.EnvironmentVariables.Add("CORECLR_PROFILER_PATH", libraryName);
        ArgumentNullException.ThrowIfNullOrEmpty(Configuration.MobileOptions?.Device);

        if (Configuration.MobileOptions.IsDevice) {
            var forwardedPorts = new List<int>() { Configuration.MobileOptions.Port };
            if (Configuration.MobileOptions.TcpTunnel != null)
                forwardedPorts.AddRange(Configuration.MobileOptions.TcpTunnel);

            var proxyProcess = MonoLauncher.TcpTunnel(Configuration.MobileOptions.Device, forwardedPorts, ProcessLogger);
            Disposables.Add(() => proxyProcess.Terminate());

            MonoLauncher.InstallDev(Configuration.MobileOptions.Device, Configuration.Program, ProcessLogger);
            var devProcess = MonoLauncher.LaunchDev(
                Configuration.MobileOptions.Device, Configuration.Program,
                Configuration.EnvironmentVariables, ProcessLogger
            ).Start();

            devProcess.AddFinalizer(() => Protocol.SendEvent(new TerminatedEvent()));
            Disposables.Add(() => SafeExtensions.Invoke(() => devProcess.Terminate()));
        }
        else {
            var simProcess = MonoLauncher.LaunchSim(
                Configuration.MobileOptions.Device, Configuration.Program,
                Configuration.EnvironmentVariables, ProcessLogger
            ).Start();

            simProcess.AddFinalizer(() => Protocol.SendEvent(new TerminatedEvent()));
            Disposables.Add(() => SafeExtensions.Invoke(() => simProcess.Terminate(entireProcessTree: true)));
        }
    }
    private void LaunchAndroid() {
        ArgumentNullException.ThrowIfNullOrEmpty(Configuration.MobileOptions?.Device);
        Configuration.EnvironmentVariables.Add("CORECLR_PROFILER_PATH", "libvsdbgremotecoreclrtarget.so");

        var applicationId = Configuration.GetApplicationName();
        if (!Configuration.MobileOptions.IsDevice)
            Configuration.MobileOptions.Device = AndroidEmulator.Run(Configuration.MobileOptions.Device, ProcessLogger).Serial;

        var forwardedPorts = new List<int>() { Configuration.MobileOptions.Port };
        if (Configuration.MobileOptions.TcpTunnel != null)
            forwardedPorts.AddRange(Configuration.MobileOptions.TcpTunnel);

        foreach (var port in forwardedPorts)
            AndroidDebugBridge.Forward(Configuration.MobileOptions.Device, port);
        if (Configuration.MobileOptions.UninstallApp)
            AndroidDebugBridge.Uninstall(Configuration.MobileOptions.Device, applicationId, ProcessLogger);

        AndroidDebugBridge.Install(Configuration.MobileOptions.Device, Configuration.Program, ProcessLogger);
        AndroidDebugBridge.Shell(Configuration.MobileOptions.Device, "setprop", "debug.coreclr.enabled", "1");
        // AndroidDebugBridge.Shell(Configuration.MobileOptions.Device, "setprop", "debug.mono.extra", "debug=<ip>:<port>,timeout=1787672357,loglevel=0,server=y"); // Legacy?
        AndroidDebugBridge.Shell(Configuration.MobileOptions.Device, "am", "set-debug-app", applicationId);
        AndroidFastDev.TryPushAssemblies(Configuration.MobileOptions.Device, Configuration.MobileOptions.AssetsPath, applicationId, ProcessLogger);
        AndroidFastDev.TrySetEnvironment(Configuration.MobileOptions.Device, Configuration.EnvironmentVariables, Configuration.MobileOptions.AssetsPath, applicationId, ProcessLogger);
        AndroidDebugBridge.Launch(Configuration.MobileOptions.Device, applicationId, ProcessLogger);

        AndroidDebugBridge.Flush(Configuration.MobileOptions.Device);
        var logcatProcess = AndroidDebugBridge.Logcat(Configuration.MobileOptions.Device, applicationId, ProcessLogger);
        Disposables.Add(() => logcatProcess.Terminate());
        Disposables.Add(() => AndroidDebugBridge.RemoveForward(Configuration.MobileOptions.Device));
    }

    private string GetCoreclrHostLibrary() {
        var runtime = $"{RuntimeInfo.GetOperationSystem()}-{RuntimeInfo.GetArchitecture()}";
        var libraryName = $"libremotemscordbihost{RuntimeInfo.LibExtension}";
        var libraryPath = Path.Combine(Configuration.RemoteHostDirectory!, runtime, libraryName);
        if (!File.Exists(libraryPath))
            throw new FileNotFoundException($"File not found: {libraryPath}");

        return libraryPath;
    }
    private RemoteAttachInfo GetAttachInfo() {
        ArgumentNullException.ThrowIfNull(Configuration.MobileOptions);

        if (Configuration.MobileOptions.Port <= 0)
            Configuration.MobileOptions.Port = RuntimeInfo.GetFreePort();
        if (Configuration.MobileOptions.Address == null)
            Configuration.MobileOptions.Address = "127.0.0.1";
        if (Configuration.MobileOptions.RuntimeIdentifier == null)
            Configuration.MobileOptions.RuntimeIdentifier = string.Empty;

        var mscordbiPath = GetCoreclrHostLibrary();
        var assembliesPath = $"{Configuration.MobileOptions.AssetsPath};{Path.GetDirectoryName(mscordbiPath)}";
        if (Configuration.MobileOptions.Platform == DebugTarget.Android)
            assembliesPath = $"{AndroidFastDev.GetAssembliesPath(Configuration.MobileOptions.AssetsPath)};{Path.GetDirectoryName(mscordbiPath)}";

        var platform = Configuration.MobileOptions.RuntimeIdentifier.Replace('-', ';').Replace("iossimulator", "ios");
        return new RemoteAttachInfo(Configuration.MobileOptions.Address, Configuration.MobileOptions.Port, platform, mscordbiPath, assembliesPath) {
            IsServer = Configuration.MobileOptions.IsServer
        };
    }
}
