using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugexceptiondebugevent-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("AF79EC94-4752-419C-A626-5FB1CC1A5AB7")]
public partial interface ICorDebugExceptionDebugEvent : ICorDebugDebugEvent {
    [PreserveSig]
    int TryGetStackPointer(out CordbAddress pStackPointer);

    [PreserveSig]
    int TryGetNativeIP(out CordbAddress pIP);

    [PreserveSig]
    int TryGetFlags(out CorDebugExceptionFlags pdwFlags);

}