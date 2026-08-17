using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugtype2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("E6E91D79-693D-48BC-B417-8284B4F10FB5")]
public partial interface ICorDebugType2 {
    [PreserveSig]
    int TryGetTypeID(out CorTypeId id);
}