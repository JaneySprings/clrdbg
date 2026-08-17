using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/interfaces/imetadatatables-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("D8F579AB-402D-4B8E-82D9-5D63B1065C68")]
public partial interface IMetaDataTables {
    [PreserveSig]
    int TryGetStringHeapSize(out uint pcbStrings);

    [PreserveSig]
    int TryGetBlobHeapSize(out uint pcbBlobs);

    [PreserveSig]
    int TryGetGuidHeapSize(out uint pcbGuids);

    [PreserveSig]
    int TryGetUserStringHeapSize(out uint pcbBlobs);

    [PreserveSig]
    int TryGetNumTables(out uint pcTables);

    [PreserveSig]
    int TryGetTableIndex(uint token, out uint pixTbl);

    [PreserveSig]
    unsafe int TryGetTableInfo(uint ixTbl, out uint pcbRow, out uint pcRows, out uint pcCols, out uint piKey, out sbyte* ppName);

    [PreserveSig]
    unsafe int TryGetColumnInfo(uint ixTbl, uint ixCol, out uint poCol, out uint pcbCol, out uint pType, out sbyte* ppName);

    [PreserveSig]
    unsafe int TryGetCodedTokenInfo(uint ixCdTkn, out uint pcTokens, out uint* ppTokens, out sbyte* ppName);

    [PreserveSig]
    int TryGetRow(uint ixTbl, uint rid, out nint ppRow);

    [PreserveSig]
    int TryGetColumn(uint ixTbl, uint ixCol, uint rid, out uint pVal);

    [PreserveSig]
    unsafe int TryGetString(uint ixString, out sbyte* ppString);

    [PreserveSig]
    int TryGetBlob(uint ixBlob, out uint pcbData, out nint ppData);

    [PreserveSig]
    unsafe int TryGetGuid(uint ixGuid, out Guid* ppGUID);

    [PreserveSig]
    int TryGetUserString(uint ixUserString, out uint pcbData, out nint ppData);

    [PreserveSig]
    int TryGetNextString(uint ixString, out uint pNext);

    [PreserveSig]
    int TryGetNextBlob(uint ixBlob, out uint pNext);

    [PreserveSig]
    int TryGetNextGuid(uint ixGuid, out uint pNext);

    [PreserveSig]
    int TryGetNextUserString(uint ixUserString, out uint pNext);
}