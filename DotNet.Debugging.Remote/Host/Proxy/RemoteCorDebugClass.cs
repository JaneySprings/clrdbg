using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Remote.Proxy;

// A class's module and token never change: each is asked for once
[GeneratedComClass]
internal partial class RemoteCorDebugClass : RemoteObject, ICorDebugClass, ICorDebugClass2 {
    private ICorDebugModule? module;
    private uint token;

    public RemoteCorDebugClass(RemoteSession session, uint handle) : base(session, handle) { }

    public int TryGetModule(out ICorDebugModule pModule) {
        if (module != null) {
            pModule = module;
            return Cor.S_OK;
        }
        var hr = InvokeObject(Iids.Class, 3, out pModule);
        if (hr == Cor.S_OK)
            module = pModule;
        return hr;
    }
    public int TryGetToken(out TypeDefToken pTypeDef) {
        if (token != 0) {
            pTypeDef = new TypeDefToken(token);
            return Cor.S_OK;
        }
        var hr = InvokeUInt32(Iids.Class, 4, out var value);
        pTypeDef = new TypeDefToken(value);
        if (hr == Cor.S_OK)
            token = value;
        return hr;
    }
}
