using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugcode3-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("D13D3E88-E1F2-4020-AA1D-3D162DCBE966")]
public partial interface ICorDebugCode3 {
    [PreserveSig]
    int TryGetReturnValueLiveOffset(uint ILoffset, uint bufferSize, out uint pFetched, [Out][MarshalUsing(CountElementName = "bufferSize")] uint[] pOffsets);
}