using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugilcode2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("46586093-D3F5-4DB6-ACDB-955BCE228C15")]
public partial interface ICorDebugILCode2 {
    [PreserveSig]
    int TryGetLocalVarSigToken(out SignatureToken pmdSig);

    [PreserveSig]
    int TryGetInstrumentedILMap(uint cMap, out uint pcMap, [Out][MarshalUsing(CountElementName = "cMap")] CorIlMap[]? map);
}