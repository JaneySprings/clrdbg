using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebuginternalframe-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("B92CC7F7-9D2D-45C4-BC2B-621FCC9DFBF4")]
public partial interface ICorDebugInternalFrame : ICorDebugFrame {
    [PreserveSig]
    int TryGetFrameType(out CorDebugInternalFrameType pType);

}