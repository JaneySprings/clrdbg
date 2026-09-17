namespace DotNet.Debugging.Remote;

// The agent's contract with its launcher: the environment that tells it where the debugger is, and the file names of
// the two libraries
public static class RemoteAgent {
    // The debugger's address and port, and whether the app is the server (listens) or connects out to the debugger
    public const string AddressVariable = "CORECLR_REMOTE_DEBUGGER_IP";
    public const string PortVariable = "CORECLR_REMOTE_DEBUGGER_PORT";
    public const string IsServerVariable = "CORECLR_REMOTE_DEBUGGER_ISSERVER";

    // The agent is loaded through the runtime's profiler hook and answers any profiler id its class factory is asked for
    public const string TargetLibraryName = "remotecoreclrtarget";
    public const string HostLibraryName = "remotecoreclrhost";
    // The wire protocol both sides speak (docs/remote/protocol.md)
    public const uint ProtocolVersion = 4;
}
