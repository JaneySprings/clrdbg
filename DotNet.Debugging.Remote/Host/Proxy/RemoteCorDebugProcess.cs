using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.Remote.Protocol;

namespace DotNet.Debugging.Remote.Proxy;

// The process the agent debugs, as the debugger sees it. Detaching closes the connection, which lets the agent continue
// whatever it held stopped; terminating goes through the agent's own request, which answers before the process ends.
// While the attach is replayed to the debugger the process stays stopped: its Continue after each replayed callback is
// answered and not made
[GeneratedComClass]
internal partial class RemoteCorDebugProcess : RemoteObject, ICorDebugProcess {
    private uint processId;

    public RemoteCorDebugProcess(RemoteSession session, uint handle) : base(session, handle) { }

    public int TryContinue(bool fIsOutOfBand) {
        if (Session.TryHoldContinue())
            return Cor.S_OK;
        return Invoke(Iids.Controller, 4, RemoteArgument.Bool(fIsOutOfBand));
    }
    public int TryDetach() {
        Session.Detach();
        return Cor.S_OK;
    }
    public int TryTerminate(uint exitCode) {
        return Forward(() => Session.Client.TerminateAsync((int)exitCode), "Terminate");
    }
    public int TryGetID(out uint pdwProcessId) {
        if (processId != 0) {
            pdwProcessId = processId;
            return Cor.S_OK;
        }
        var hr = InvokeUInt32(Iids.Process, 13, out pdwProcessId);
        if (hr == Cor.S_OK)
            processId = pdwProcessId;
        return hr;
    }
}
