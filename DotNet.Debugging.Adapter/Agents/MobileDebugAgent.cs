using DotNet.Debugging.Engine;

namespace DotNet.Debugging.Adapter;

/// <summary>
/// Drives a CoreCLR mobile (iOS simulator / maccatalyst) debug session. Unlike a local launch, the app is started
/// out-of-process on the target and connects back to the debugger's remote transport, so ordering matters:
/// the debugger must be listening before the app is launched. That is arranged via the <c>onListenerReady</c>
/// callback passed to <see cref="ManagedDebugger.AttachRemote"/>.
/// </summary>
public class MobileDebugAgent : BaseDebugAgent<LaunchConfiguration> {
    // private CoreClrMobileTarget? _target;
    // private int _debuggerPort;

    public MobileDebugAgent(LaunchConfiguration configuration, DebugSession debugSession) : base(configuration, debugSession) { }

    public override void Connect(ManagedDebugger debugger) {
        // ArgumentNullException.ThrowIfNull(Configuration.MobileOptions);
        // _debuggerPort = RuntimeInfo.GetFreePort();
        // _target = CoreClrMobileTarget.Resolve(
        //     Configuration.Program,
        //     Configuration.MobileOptions.Platform!,
        //     Configuration.MobileOptions.RuntimeIdentifier!,
        //     Configuration.MobileOptions.IsSimulator,
        //     Configuration.MobileOptions.Device,
        //     Configuration.MobileOptions.VsdbgRemoteResources);
        // Logger.Debug($"Prepared mobile target {_target.DbgShimPlatform}: bundle={_target.AppBundlePath}, port={_debuggerPort}");

        // var remoteAttachInfo = new RemoteAttachInfo {
        //     Address = "127.0.0.1",
        //     Port = _debuggerPort,
        //     Platform = _target.DbgShimPlatform,
        //     IsServer = true,
        //     MscordbiPath = _target.MscordbiPath,
        //     AssembliesPath = _target.AssembliesPath
        // };
        // debugger.AttachRemote(remoteAttachInfo, Configuration.JustMyCode, onListenerReady: PrepareTarget);
    }

    private void PrepareTarget() {
        // ArgumentNullException.ThrowIfNull(_target);
        // Logger.Debug($"Debugger listening on port {_debuggerPort}, launching app on {(_target.IsMacCatalyst ? "maccatalyst" : "iOS simulator")}");

        // // DebugSession is an IProcessLogger, so the app's console is forwarded straight to the debug console.
        // var debuggeeProcess = CoreClrMobileLauncher.Launch(_target, _debuggerPort, DebugSession);

        // // The remote transport only reports process exit once connected; watch the launcher process too so a
        // // crash-before-connect (or the simulator/app being closed) still terminates the session cleanly.
        // try {
        //     debuggeeProcess.EnableRaisingEvents = true;
        //     debuggeeProcess.Exited += (_, _) => DebugSession.Protocol.SendEvent(new TerminatedEvent());
        // }
        // catch (Exception ex) {
        //     Logger.Error($"Failed to watch the debuggee process: {ex.Message}");
        // }

        // Disposables.Add(() => {
        //     try {
        //         if (debuggeeProcess is { HasExited: false }) debuggeeProcess.Kill(entireProcessTree: true);
        //     }
        //     catch (Exception ex) {
        //         Logger.Error($"Failed to kill the debuggee process: {ex.Message}");
        //     }
        // });
    }
}
