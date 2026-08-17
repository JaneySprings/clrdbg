using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugcode2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("5F696509-452F-4436-A3FE-4D11FE7E2347")]
public partial interface ICorDebugCode2 {
    [PreserveSig]
    int TryGetCodeChunks(uint cbufSize, out uint pcnumChunks, [Out][MarshalUsing(CountElementName = "cbufSize")] CodeChunkInfo[]? chunks);

    [PreserveSig]
    int TryGetCompilerFlags(out uint pdwFlags);
}