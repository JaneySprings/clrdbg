using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugheapvalue-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAFA-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugHeapValue : ICorDebugValue {
    [PreserveSig]
    int TryIsValid([MarshalAs(UnmanagedType.Bool)] out bool pbValid);

    [PreserveSig]
    int TryCreateRelocBreakpoint(out ICorDebugValueBreakpoint ppBreakpoint);

}