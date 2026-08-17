using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugeditandcontinuesnapshot-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("6DC3FA01-D7CB-11D2-8A95-0080C792E5D8")]
public partial interface ICorDebugEditAndContinueSnapshot {
    [PreserveSig]
    int TryCopyMetaData(nint pIStream, out Guid pMvid);

    [PreserveSig]
    int TryGetMvid(out Guid pMvid);

    [PreserveSig]
    int TryGetRoDataRVA(out uint pRoDataRVA);

    [PreserveSig]
    int TryGetRwDataRVA(out uint pRwDataRVA);

    [PreserveSig]
    int TrySetPEBytes(nint pIStream);

    [PreserveSig]
    int TrySetILMap(MetadataToken mdFunction, uint cMapSize, [In][MarshalUsing(CountElementName = "cMapSize")] CorIlMap[] map);

    [PreserveSig]
    int TrySetPESymbolBytes(nint pIStream);
}