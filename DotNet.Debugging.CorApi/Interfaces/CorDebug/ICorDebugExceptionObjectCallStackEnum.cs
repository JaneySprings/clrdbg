using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugexceptionobjectcallstackenum-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("ED775530-4DC4-41F7-86D0-9E2DEF7DFC66")]
public partial interface ICorDebugExceptionObjectCallStackEnum : ICorDebugEnum {
    [PreserveSig]
    int TryNext(uint celt, [Out][MarshalUsing(CountElementName = "celt")] CorDebugExceptionObjectStackFrame[] values, out uint pceltFetched);

}