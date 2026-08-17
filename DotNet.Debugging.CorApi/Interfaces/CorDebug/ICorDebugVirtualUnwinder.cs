using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugvirtualunwinder-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("F69126B7-C787-4F6B-AE96-A569786FC670")]
public partial interface ICorDebugVirtualUnwinder {
    [PreserveSig]
    int TryGetContext(uint contextFlags, uint cbContextBuf, out uint contextSize, [Out][MarshalUsing(CountElementName = "cbContextBuf")] byte[] contextBuf);

    [PreserveSig]
    int TryNext();
}