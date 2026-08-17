using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugobjectvalue2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("49E4A320-4A9B-4ECA-B105-229FB7D5009F")]
public partial interface ICorDebugObjectValue2 {
    [PreserveSig]
    int TryGetVirtualMethodAndType(MemberRefToken memberRef, out ICorDebugFunction ppFunction, out ICorDebugType ppType);
}