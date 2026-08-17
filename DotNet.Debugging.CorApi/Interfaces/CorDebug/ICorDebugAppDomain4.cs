using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugappdomain4-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("FB99CC40-83BE-4724-AB3B-768E796EBAC2")]
public partial interface ICorDebugAppDomain4 {
    [PreserveSig]
    int TryGetObjectForCCW(CordbAddress ccwPointer, out ICorDebugValue ppManagedObject);
}