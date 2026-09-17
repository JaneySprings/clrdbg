using System.Runtime.InteropServices;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.Remote.Protocol;

namespace DotNet.Debugging.Remote.Proxy;

// A value with bytes the debugger reads and writes through a buffer of its own (ICorDebugGenericValue), of the size
// the value reports; the primitive and the value type proxies share it
public abstract class RemoteValueBytes : RemoteObject {
    private uint valueSize;

    protected RemoteValueBytes(RemoteSession session, uint handle) : base(session, handle) { }

    public int TryGetValue(nint pTo) {
        var hr = GetValueSize(out var size);
        if (hr != Cor.S_OK)
            return hr;
        var bytes = RemoteArgument.OutBytes(size);
        hr = Invoke(Iids.GenericValue, 7, bytes);
        if (hr == Cor.S_OK)
            Marshal.Copy(bytes.BytesResult, 0, pTo, Math.Min(bytes.BytesResult.Length, (int)size));
        return hr;
    }
    public int TrySetValue(nint pFrom) {
        var hr = GetValueSize(out var size);
        if (hr != Cor.S_OK)
            return hr;
        var bytes = new byte[size];
        Marshal.Copy(pFrom, bytes, 0, bytes.Length);
        return Invoke(Iids.GenericValue, 8, RemoteArgument.Bytes(bytes));
    }

    private int GetValueSize(out uint size) {
        if (valueSize != 0) {
            size = valueSize;
            return Cor.S_OK;
        }
        var hr = InvokeUInt32(Iids.Value, 4, out size);
        if (hr == Cor.S_OK)
            valueSize = size;
        return hr;
    }
}
