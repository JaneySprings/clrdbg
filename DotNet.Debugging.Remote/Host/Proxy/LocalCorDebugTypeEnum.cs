using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Remote.Proxy;

// An enumerator over types the host already holds: the parameters of a type never change, so they are read from the
// agent once (RemoteCorDebugType) and enumerated here without a round trip
[GeneratedComClass]
internal partial class LocalCorDebugTypeEnum : ICorDebugTypeEnum {
    private readonly ICorDebugType[] items;
    private int position;

    public LocalCorDebugTypeEnum(ICorDebugType[] items) {
        this.items = items;
    }

    public int TrySkip(uint celt) {
        position = (int)Math.Min((long)items.Length, position + (long)celt);
        return Cor.S_OK;
    }
    public int TryReset() {
        position = 0;
        return Cor.S_OK;
    }
    public int TryClone(out ICorDebugEnum ppEnum) {
        var clone = new LocalCorDebugTypeEnum(items);
        clone.position = position;
        ppEnum = clone;
        return Cor.S_OK;
    }
    public int TryGetCount(out uint pcelt) {
        pcelt = (uint)items.Length;
        return Cor.S_OK;
    }
    // S_FALSE when fewer items than asked for were left, as an enumerator of the runtime answers
    public int TryNext(uint celt, ICorDebugType[] values, out uint pceltFetched) {
        var fetched = 0;
        while (fetched < celt && position < items.Length && values != null && fetched < values.Length) {
            values[fetched] = items[position];
            fetched++;
            position++;
        }
        pceltFetched = (uint)fetched;
        return fetched == celt ? Cor.S_OK : Cor.S_FALSE;
    }
}
