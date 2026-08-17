using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugvalue-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAF7-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugValue {
    [PreserveSig]
    int TryGetType(out CorElementType pType);

    [PreserveSig]
    int TryGetSize(out uint pSize);

    [PreserveSig]
    int TryGetAddress(out CordbAddress pAddress);

    [PreserveSig]
    int TryCreateBreakpoint(out ICorDebugValueBreakpoint ppBreakpoint);
}