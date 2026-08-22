namespace DotNet.Debugging.Engine.Models;

// Everything dbgshim needs to build an ICorDebug bound to a remote CoreCLR target (iOS/maccatalyst/Android).
// The debugger never opens a socket itself - the remote transport lives inside the target-matched libmscordbi
public class RemoteAttachInfo {
    // IP the transport binds to (server) or connects to (client)
    public string Address { get; set; }
    public int Port { get; set; }
    // Target platform string dbgshim expects, e.g. "maccatalyst;arm64" or "ios;arm64"
    public string Platform { get; set; }
    // When true the debugger listens and the on-device runtime connects to it
    public bool IsServer { get; set; }
    // Absolute path to the target-matched libmscordbi
    public string MscordbiPath { get; set; }
    // ';'-separated directories dbgshim searches for the debuggee assemblies/symbols and the remote host
    public string AssembliesPath { get; set; }

    public RemoteAttachInfo(string address, int port, string platform, string mscordbiPath, string assembliesPath) {
        Address = address;
        Port = port;
        Platform = platform;
        MscordbiPath = mscordbiPath;
        AssembliesPath = assembliesPath;
    }
}
