using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebuginternalframe2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("C0815BDC-CFAB-447E-A779-C116B454EB5B")]
public partial interface ICorDebugInternalFrame2 {
    [PreserveSig]
    int TryGetAddress(out CordbAddress pAddress);

    [PreserveSig]
    int TryIsCloserToLeaf(ICorDebugFrame pFrameToCompare, [MarshalAs(UnmanagedType.Bool)] out bool pIsCloser);
}