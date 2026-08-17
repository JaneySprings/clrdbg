using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugstackwalk-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("A0647DE9-55DE-4816-929C-385271C64CF7")]
public partial interface ICorDebugStackWalk {
    [PreserveSig]
    int TryGetContext(uint contextFlags, uint contextBufSize, out uint contextSize, [Out][MarshalUsing(CountElementName = "contextBufSize")] byte[] contextBuf);

    [PreserveSig]
    int TrySetContext(CorDebugSetContextFlag flag, uint contextSize, [In][MarshalUsing(CountElementName = "contextSize")] byte[] context);

    [PreserveSig]
    int TryNext();

    [PreserveSig]
    int TryGetFrame(out ICorDebugFrame pFrame);
}