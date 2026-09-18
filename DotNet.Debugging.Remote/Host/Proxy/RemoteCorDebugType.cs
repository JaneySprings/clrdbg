using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Remote.Proxy;

// A type never changes: its element type, class, base and rank are asked for once (generated), and so are its type
// parameters, which a debugger enumerates for every value it shows
[GeneratedComClass]
internal partial class RemoteCorDebugType : RemoteObject, ICorDebugType, ICorDebugType2 {
    private ICorDebugType[]? typeParameters;

    public RemoteCorDebugType(RemoteSession session, uint handle) : base(session, handle) { }

    public int TryEnumerateTypeParameters(out ICorDebugTypeEnum ppTyParEnum) {
        ppTyParEnum = null!;
        if (typeParameters == null) {
            var hr = InvokeObject(Iids.Type, 5, out ICorDebugTypeEnum remote);
            if (hr != Cor.S_OK)
                return hr;
            hr = remote.TryGetCount(out var count);
            if (hr != Cor.S_OK)
                return hr;
            var items = new ICorDebugType[count];
            if (count > 0) {
                hr = remote.TryNext(count, items, out var fetched);
                if (hr < 0)
                    return hr;
                if (fetched < count)
                    Array.Resize(ref items, (int)fetched);
            }
            typeParameters = items;
        }
        ppTyParEnum = new LocalCorDebugTypeEnum(typeParameters);
        return Cor.S_OK;
    }
}
