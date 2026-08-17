using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugassembly2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("426D1F9E-6DD4-44C8-AEC7-26CDBAF4E398")]
public partial interface ICorDebugAssembly2 {
    [PreserveSig]
    int TryIsFullyTrusted([MarshalAs(UnmanagedType.Bool)] out bool pbFullyTrusted);
}