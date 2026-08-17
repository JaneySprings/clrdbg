using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugnativeframe2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("35389FF1-3684-4C55-A2EE-210F26C60E5E")]
public partial interface ICorDebugNativeFrame2 {
    [PreserveSig]
    int TryIsChild([MarshalAs(UnmanagedType.Bool)] out bool pIsChild);

    [PreserveSig]
    int TryIsMatchingParentFrame(ICorDebugNativeFrame2 pPotentialParentFrame, [MarshalAs(UnmanagedType.Bool)] out bool pIsParent);

    [PreserveSig]
    int TryGetStackParameterSize(out uint pSize);
}