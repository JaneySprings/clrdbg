using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugthread4-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("1A1F204B-1C66-4637-823F-3EE6C744A69C")]
public partial interface ICorDebugThread4 {
    [PreserveSig]
    int TryHasUnhandledException();

    [PreserveSig]
    int TryGetBlockingObjects(out ICorDebugBlockingObjectEnum ppBlockingObjectEnum);

    [PreserveSig]
    int TryGetCurrentCustomDebuggerNotification(out ICorDebugValue ppNotificationObject);
}