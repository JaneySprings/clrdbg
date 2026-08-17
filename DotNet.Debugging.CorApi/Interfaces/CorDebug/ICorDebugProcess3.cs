using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugprocess3-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("2EE06488-C0D4-42B1-B26D-F3795EF606FB")]
public partial interface ICorDebugProcess3 {
    [PreserveSig]
    int TrySetEnableCustomNotification(ICorDebugClass pClass, [MarshalAs(UnmanagedType.Bool)] bool fEnable);
}