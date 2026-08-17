using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/interfaces/imetadatainfo-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("7998EA64-7F95-48B8-86FC-17CAF48BF5CB")]
public partial interface IMetaDataInfo {
    [PreserveSig]
    int TryGetFileMapping(out nint ppvData, out ulong pcbData, out uint pdwMappingType);
}