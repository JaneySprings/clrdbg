using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugvalue3-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("565005FC-0F8A-4F3E-9EDB-83102B156595")]
public partial interface ICorDebugValue3 {
    [PreserveSig]
    int TryGetSize64(out ulong pSize);
}