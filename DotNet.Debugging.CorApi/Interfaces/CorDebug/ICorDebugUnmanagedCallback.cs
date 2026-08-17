using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugunmanagedcallback-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("5263E909-8CB5-11D3-BD2F-0000F80849BD")]
public partial interface ICorDebugUnmanagedCallback {
    [PreserveSig]
    int TryDebugEvent(nint pDebugEvent, [MarshalAs(UnmanagedType.Bool)] bool fOutOfBand);
}