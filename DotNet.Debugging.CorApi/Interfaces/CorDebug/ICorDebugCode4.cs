using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugcode4-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("18221FA4-20CB-40FA-B19D-9F91C4FA8C14")]
public partial interface ICorDebugCode4 {
    [PreserveSig]
    int TryEnumerateVariableHomes(out ICorDebugVariableHomeEnum ppEnum);
}