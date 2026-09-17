namespace DotNet.Debugging.Remote;

// What the agent answers to Hello
public class RemoteAgentInfo {
    public uint ProtocolVersion { get; }
    public int ProcessId { get; }
    public string RuntimeDirectory { get; }
    // A handle of the debugged process when this host missed the attach callbacks (it connected after they went to an
    // earlier host, or to none), for it to replay the attach from; zero when the callbacks are coming
    public uint ProcessHandle { get; }

    public RemoteAgentInfo(uint protocolVersion, int processId, string runtimeDirectory, uint processHandle) {
        ProtocolVersion = protocolVersion;
        ProcessId = processId;
        RuntimeDirectory = runtimeDirectory;
        ProcessHandle = processHandle;
    }
}
