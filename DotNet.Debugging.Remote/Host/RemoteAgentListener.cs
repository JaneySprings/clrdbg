using System.Net;
using System.Net.Sockets;

namespace DotNet.Debugging.Remote;

// The debugger's end when it is the server: listening from the moment the launch asks for it, so an app that connects
// out right after its start finds it, and handing over the one agent that connects
public class RemoteAgentListener : IDisposable {
    private readonly TcpListener listener;

    public RemoteAgentListener(IPAddress address, int port) {
        listener = new TcpListener(address, port);
    }

    public void Start() {
        listener.Start();
    }
    public async Task<RemoteDebuggerClient> AcceptAsync(CancellationToken cancellationToken = default) {
        var accepted = await listener.AcceptTcpClientAsync(cancellationToken);
        return RemoteDebuggerClient.FromConnected(accepted);
    }

    public void Dispose() {
        listener.Stop();
    }
}
