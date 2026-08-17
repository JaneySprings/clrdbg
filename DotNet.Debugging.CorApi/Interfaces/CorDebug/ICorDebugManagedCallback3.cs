using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback3-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("264EA0FC-2591-49AA-868E-835E6515323F")]
public partial interface ICorDebugManagedCallback3 {
    [PreserveSig]
    int TryCustomNotification(ICorDebugThread pThread, ICorDebugAppDomain pAppDomain);
}