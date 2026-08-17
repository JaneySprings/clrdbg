using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("72965963-34FD-46E9-9434-B817FE6E7F43")]
public partial interface ICorDebugFunction4 {
    [PreserveSig]
    int TryCreateNativeBreakpoint(out ICorDebugFunctionBreakpoint ppBreakpoint);
}