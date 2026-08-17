using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugclass2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("B008EA8D-7AB1-43F7-BB20-FBB5A04038AE")]
public partial interface ICorDebugClass2 {
    [PreserveSig]
    int TryGetParameterizedType(CorElementType elementType, uint nTypeArgs, [In][MarshalUsing(CountElementName = "nTypeArgs")] ICorDebugType[] ppTypeArgs, out ICorDebugType ppType);

    [PreserveSig]
    int TrySetJMCStatus([MarshalAs(UnmanagedType.Bool)] bool bIsJustMyCode);
}