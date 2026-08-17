using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/interfaces/imetadatatables2-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("BADB5F70-58DA-43A9-A1C6-D74819F19B15")]
public unsafe partial interface IMetaDataTables2 : IMetaDataTables {
    [PreserveSig]
    int TryGetMetaDataStorage(out nint ppvMd, out uint pcbMd);

    [PreserveSig]
    unsafe int TryGetMetaDataStreamInfo(uint ix, out sbyte* ppchName, out nint ppv, out uint pcb);

}