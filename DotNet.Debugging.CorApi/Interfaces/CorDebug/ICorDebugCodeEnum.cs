using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugcodeenum-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("55E96461-9645-45E4-A2FF-0367877ABCDE")]
public partial interface ICorDebugCodeEnum : ICorDebugEnum {
    [PreserveSig]
    int TryNext(uint celt, [Out][MarshalUsing(CountElementName = "celt")] ICorDebugCode[] values, out uint pceltFetched);

}