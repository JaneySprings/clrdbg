using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebuggenericvalue-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAF8-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugGenericValue : ICorDebugValue {
    [PreserveSig]
    int TryGetValue(nint pTo);

    [PreserveSig]
    int TrySetValue(nint pFrom);

}