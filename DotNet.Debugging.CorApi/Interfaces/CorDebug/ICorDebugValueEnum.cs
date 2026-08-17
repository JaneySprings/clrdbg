using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugvalueenum-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCB0A-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugValueEnum : ICorDebugEnum {
    [PreserveSig]
    int TryNext(uint celt, [Out][MarshalUsing(CountElementName = "celt")] ICorDebugValue[] values, out uint pceltFetched);

}