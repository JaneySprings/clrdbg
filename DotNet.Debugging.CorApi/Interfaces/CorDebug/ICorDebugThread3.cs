using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugthread3-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("F8544EC3-5E4E-46C7-8D3E-A52B8405B1F5")]
public partial interface ICorDebugThread3 {
    [PreserveSig]
    int TryCreateStackWalk(out ICorDebugStackWalk ppStackWalk);

    [PreserveSig]
    int TryGetActiveInternalFrames(uint cInternalFrames, out uint pcInternalFrames, [In][Out][MarshalUsing(CountElementName = "cInternalFrames")] ICorDebugInternalFrame2[] ppInternalFrames);
}