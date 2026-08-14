using System.Diagnostics;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.Soft.Extensions;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Soft;

public class NoDebugLaunchAgent : BaseLaunchAgent {
    public NoDebugLaunchAgent(LaunchConfiguration configuration) : base(configuration) { }

    public override void Launch(DebugSession debugSession) {
        var launchInfo = Configuration.GetLaunchInfo();
        var processStartInfo = new ProcessStartInfo {
            FileName = launchInfo.Program,
            WorkingDirectory = launchInfo.Cwd ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in launchInfo.Arguments)
            processStartInfo.ArgumentList.Add(argument);
        foreach (var env in launchInfo.Env)
            processStartInfo.Environment[env.Key] = env.Value;

        var process = Process.Start(processStartInfo);
        if (process == null)
            throw ServerExtensions.GetProtocolException($"Failed to start the application: '{Configuration.ProgramPath}'");

        process.OutputDataReceived += (_, e) => {
            if (e.Data != null)
                debugSession.OnOutputDataReceived(e.Data);
        };
        process.ErrorDataReceived += (_, e) => {
            if (e.Data != null)
                debugSession.OnErrorDataReceived(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => {
            debugSession.Protocol.TrySendEvent(new TerminatedEvent());
        };

        Disposables.Add(() => {
            if (!process.HasExited)
                process.Kill();
        });
    }
    public override void Connect(ManagedDebugger debugger) { }
}
