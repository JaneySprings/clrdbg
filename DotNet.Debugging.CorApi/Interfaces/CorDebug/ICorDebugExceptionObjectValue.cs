using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugexceptionobjectvalue-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("AE4CA65D-59DD-42A2-83A5-57E8A08D8719")]
public partial interface ICorDebugExceptionObjectValue {
    [PreserveSig]
    int TryEnumerateExceptionCallStack(out ICorDebugExceptionObjectCallStackEnum ppCallStackEnum);
}