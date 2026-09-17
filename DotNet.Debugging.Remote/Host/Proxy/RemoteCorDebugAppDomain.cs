using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.Remote.Protocol;

namespace DotNet.Debugging.Remote.Proxy;

// An app domain is a controller too: its Continue is held back like the process's while the attach is replayed
[GeneratedComClass]
internal partial class RemoteCorDebugAppDomain : RemoteObject, ICorDebugAppDomain, ICorDebugAppDomain2 {
    public RemoteCorDebugAppDomain(RemoteSession session, uint handle) : base(session, handle) { }

    public int TryContinue(bool fIsOutOfBand) {
        if (Session.TryHoldContinue())
            return Cor.S_OK;
        return Invoke(Iids.Controller, 4, RemoteArgument.Bool(fIsOutOfBand));
    }
}
