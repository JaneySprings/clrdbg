using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmodule4-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("FF8B8EAF-25CD-4316-8859-84416DE4402E")]
public partial interface ICorDebugModule4 {
    [PreserveSig]
    int TryIsMappedLayout([MarshalAs(UnmanagedType.Bool)] out bool pIsMapped);
}