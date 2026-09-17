using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Remote.Proxy;

// A thread's id never changes: it is asked for once
[GeneratedComClass]
internal partial class RemoteCorDebugThread : RemoteObject, ICorDebugThread {
    private uint threadId;

    public RemoteCorDebugThread(RemoteSession session, uint handle) : base(session, handle) { }

    public int TryGetID(out uint pdwThreadId) {
        if (threadId != 0) {
            pdwThreadId = threadId;
            return Cor.S_OK;
        }
        var hr = InvokeUInt32(Iids.Thread, 4, out pdwThreadId);
        if (hr == Cor.S_OK)
            threadId = pdwThreadId;
        return hr;
    }
}
