using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugbreakpoint-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAE8-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugBreakpoint {
    [PreserveSig]
    int TryActivate([MarshalAs(UnmanagedType.Bool)] bool bActive);

    [PreserveSig]
    int TryIsActive([MarshalAs(UnmanagedType.Bool)] out bool pbActive);
}