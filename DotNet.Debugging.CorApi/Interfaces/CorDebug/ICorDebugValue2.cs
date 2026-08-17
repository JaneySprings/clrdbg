using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugvalue2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("5E0B54E7-D88A-4626-9420-A691E0A78B49")]
public partial interface ICorDebugValue2 {
    [PreserveSig]
    int TryGetExactType(out ICorDebugType ppType);
}