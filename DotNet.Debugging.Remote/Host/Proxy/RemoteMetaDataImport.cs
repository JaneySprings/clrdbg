using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.Remote.Protocol;

namespace DotNet.Debugging.Remote.Proxy;

// The metadata importer of a module, the device's own, reached through the module's GetMetaDataInterface. A blob or a
// constant the importer points into its memory is copied here and handed out from memory of this proxy, kept for as
// long as the proxy lives, the way the importer keeps its own. Attribute lookups are what a debugger repeats most
// (DebuggerDisplay, DebuggerBrowsable, DebuggerTypeProxy of every type and member it shows): each is asked once
[GeneratedComClass]
internal partial class RemoteMetaDataImport : RemoteObject, IMetaDataImport, IMetaDataImport2, IMetaDataTables, IMetaDataTables2 {
    private readonly Dictionary<string, AttributeLookup> attributeLookups = new Dictionary<string, AttributeLookup>();

    private sealed class AttributeLookup {
        public int HResult;
        public nint Data;
        public uint Size;
    }

    public RemoteMetaDataImport(RemoteSession session, uint handle) : base(session, handle) { }

    public int TryGetCustomAttributeByName(MetadataToken tkObj, string szName, out nint ppData, out uint pcbData) {
        var key = tkObj.Value + ":" + szName;
        AttributeLookup? lookup;
        lock (attributeLookups)
            attributeLookups.TryGetValue(key, out lookup);
        if (lookup == null) {
            var blob = RemoteArgument.OutBlob(1, 0);
            lookup = new AttributeLookup();
            lookup.HResult = Invoke(Iids.MetaDataImport, 60, RemoteArgument.UInt32(tkObj.Value), RemoteArgument.Text(szName), blob);
            lookup.Data = KeepBlob(blob.BytesResult);
            lookup.Size = blob.UInt32Result;
            // A failed call may succeed later (a lost connection does not), an answer stands
            if (lookup.HResult >= 0) {
                lock (attributeLookups)
                    attributeLookups[key] = lookup;
            }
        }
        ppData = lookup.Data;
        pcbData = lookup.Size;
        return lookup.HResult;
    }
}
