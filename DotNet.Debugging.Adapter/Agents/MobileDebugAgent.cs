using DotNet.Debugging.Common;
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
        debugger.AttachRemote(GetAttachInfo(), Configuration.JustMyCode, onListenerReady: () => {
            Logger.Debug($"Debugger listening on {Configuration.MobileOptions.Address}:{Configuration.MobileOptions.Port}");

            Configuration.EnvironmentVariables.Add("CORECLR_ENABLE_PROFILING", "1");
            Configuration.EnvironmentVariables.Add("CORECLR_PROFILER", "{9DC623E8-C88F-4FD5-AD99-77E67E1D9631}");
            Configuration.EnvironmentVariables.Add("CORECLR_REMOTE_DEBUGGER_IP", Configuration.MobileOptions.Address!);
            Configuration.EnvironmentVariables.Add("CORECLR_REMOTE_DEBUGGER_PORT", Configuration.MobileOptions.Port.ToString());
            Configuration.EnvironmentVariables.Add("CORECLR_REMOTE_DEBUGGER_ISSERVER", Configuration.MobileOptions.IsServer ? "0" : "1");
            Configuration.EnvironmentVariables.Add("DOTNET_MODIFIABLE_ASSEMBLIES", "debug");

            switch (Configuration.Platform) {
                case DebugTarget.Android:
                    // LaunchAndroid();
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

        var appProcess = new ProcessRunner(AppleSdkLocator.OpenTool(), new ProcessArgumentBuilder()
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
        return new RemoteAttachInfo {
            Address = Configuration.MobileOptions.Address,
            Port = Configuration.MobileOptions.Port,
            IsServer = Configuration.MobileOptions.IsServer,
            AssembliesPath = $"{Configuration.MobileOptions.AssetsPath};{Path.GetDirectoryName(mscordbiPath)}",
            Platform = Configuration.MobileOptions.RuntimeIdentifier.Replace('-', ';').Replace("iossimulator", "ios"),
            MscordbiPath = mscordbiPath,
        };
    }
}
