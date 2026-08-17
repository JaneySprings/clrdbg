using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugfunction3-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("09B70F28-E465-482D-99E0-81A165EB0532")]
public partial interface ICorDebugFunction3 {
    [PreserveSig]
    int TryGetActiveReJitRequestILCode(out ICorDebugILCode ppReJitedILCode);
}