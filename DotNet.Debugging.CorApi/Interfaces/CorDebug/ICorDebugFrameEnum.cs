using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugframeenum-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCB07-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugFrameEnum : ICorDebugEnum {
    [PreserveSig]
    int TryNext(uint celt, [Out][MarshalUsing(CountElementName = "celt")] ICorDebugFrame[] frames, out uint pceltFetched);

}