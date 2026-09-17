using System.Runtime.InteropServices.Marshalling;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Remote.Proxy;

// The metadata importer of a module, the device's own, reached through the module's GetMetaDataInterface. A blob or a
// constant the importer points into its memory is copied here and handed out from memory of this proxy, kept for as
// long as the proxy lives, the way the importer keeps its own
[GeneratedComClass]
internal partial class RemoteMetaDataImport : RemoteObject, IMetaDataImport, IMetaDataImport2, IMetaDataTables, IMetaDataTables2 { }
