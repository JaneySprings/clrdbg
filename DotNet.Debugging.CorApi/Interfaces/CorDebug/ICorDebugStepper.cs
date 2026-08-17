using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugstepper-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAEC-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugStepper {
    [PreserveSig]
    int TryIsActive([MarshalAs(UnmanagedType.Bool)] out bool pbActive);

    [PreserveSig]
    int TryDeactivate();

    [PreserveSig]
    int TrySetInterceptMask(CorDebugIntercept mask);

    [PreserveSig]
    int TrySetUnmappedStopMask(CorDebugUnmappedStop mask);

    [PreserveSig]
    int TryStep([MarshalAs(UnmanagedType.Bool)] bool bStepIn);

    [PreserveSig]
    int TryStepRange([MarshalAs(UnmanagedType.Bool)] bool bStepIn, [In][MarshalUsing(CountElementName = "cRangeCount")] CorDebugStepRange[] ranges, uint cRangeCount);

    [PreserveSig]
    int TryStepOut();

    [PreserveSig]
    int TrySetRangeIL([MarshalAs(UnmanagedType.Bool)] bool bIL);
}