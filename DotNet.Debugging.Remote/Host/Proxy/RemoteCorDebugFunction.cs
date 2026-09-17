using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Remote.Proxy;

// A function's module, class and token never change: each is asked for once
[GeneratedComClass]
internal partial class RemoteCorDebugFunction : RemoteObject, ICorDebugFunction, ICorDebugFunction2 {
    private ICorDebugModule? module;
    private ICorDebugClass? functionClass;
    private uint token;

    public RemoteCorDebugFunction(RemoteSession session, uint handle) : base(session, handle) { }

    public int TryGetModule(out ICorDebugModule ppModule) {
        if (module != null) {
            ppModule = module;
            return Cor.S_OK;
        }
        var hr = InvokeObject(Iids.Function, 3, out ppModule);
        if (hr == Cor.S_OK)
            module = ppModule;
        return hr;
    }
    public int TryGetClass(out ICorDebugClass ppClass) {
        if (functionClass != null) {
            ppClass = functionClass;
            return Cor.S_OK;
        }
        var hr = InvokeObject(Iids.Function, 4, out ppClass);
        if (hr == Cor.S_OK)
            functionClass = ppClass;
        return hr;
    }
    public int TryGetToken(out MethodDefToken pMethodDef) {
        if (token != 0) {
            pMethodDef = new MethodDefToken(token);
            return Cor.S_OK;
        }
        var hr = InvokeUInt32(Iids.Function, 5, out var value);
        pMethodDef = new MethodDefToken(value);
        if (hr == Cor.S_OK)
            token = value;
        return hr;
    }
}
