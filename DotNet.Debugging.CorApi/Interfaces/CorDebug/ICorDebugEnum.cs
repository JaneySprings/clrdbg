using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugenum-interface1
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCB01-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugEnum {
    [PreserveSig]
    int TrySkip(uint celt);

    [PreserveSig]
    int TryReset();

    [PreserveSig]
    int TryClone(out ICorDebugEnum ppEnum);

    [PreserveSig]
    int TryGetCount(out uint pcelt);
}