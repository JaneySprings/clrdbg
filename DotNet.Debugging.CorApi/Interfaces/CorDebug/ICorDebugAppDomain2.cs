using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugappdomain2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("096E81D5-ECDA-4202-83F5-C65980A9EF75")]
public partial interface ICorDebugAppDomain2 {
    [PreserveSig]
    int TryGetArrayOrPointerType(CorElementType elementType, uint nRank, ICorDebugType pTypeArg, out ICorDebugType ppType);

    [PreserveSig]
    int TryGetFunctionPointerType(uint nTypeArgs, [In][MarshalUsing(CountElementName = "nTypeArgs")] ICorDebugType[] ppTypeArgs, out ICorDebugType ppType);
}