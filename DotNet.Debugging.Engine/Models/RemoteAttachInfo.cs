namespace DotNet.Debugging.Engine.Models;

/// <summary>
/// Everything the native <c>dbgshim!RegisterForRuntimeStartupRemotePort</c> entry point needs to
/// build an <see cref="DotNet.Debugging.CorApi.ICorDebug"/> bound to a remote CoreCLR target (iOS/maccatalyst/Android).
/// The debugger never opens a socket itself - the remote transport lives entirely inside the target-matched
/// <c>libmscordbi</c> selected by <see cref="MscordbiPath"/>.
/// </summary>
public class RemoteAttachInfo {
    /// <summary>IP the transport binds to (server) or connects to (client). Typically 127.0.0.1.</summary>
    public required string Address { get; set; }
    /// <summary>TCP port the target profiler and the debugger meet on.</summary>
    public required int Port { get; set; }
    /// <summary>Target platform string dbgshim expects, e.g. "maccatalyst;arm64" or "ios;arm64".</summary>
    public required string Platform { get; set; }
    /// <summary>When true the debugger listens (server) and the on-device runtime connects to it.</summary>
    public required bool IsServer { get; set; }
    /// <summary>Absolute path to the target-matched libmscordbi that ships in the built .app bundle.</summary>
    public required string MscordbiPath { get; set; }
    /// <summary>';'-separated directories dbgshim searches for the debuggee assemblies/symbols and the remote host.</summary>
    public required string AssembliesPath { get; set; }
}
