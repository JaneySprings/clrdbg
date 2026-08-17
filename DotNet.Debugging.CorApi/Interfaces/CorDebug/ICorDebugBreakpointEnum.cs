using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugbreakpointenum-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCB03-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugBreakpointEnum : ICorDebugEnum {
    [PreserveSig]
    int TryNext(uint celt, [Out][MarshalUsing(CountElementName = "celt")] ICorDebugBreakpoint[] breakpoints, out uint pceltFetched);

}