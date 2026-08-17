using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugdebugevent-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("41BD395D-DE99-48F1-BF7A-CC0F44A6D281")]
public partial interface ICorDebugDebugEvent {
    [PreserveSig]
    int TryGetEventKind(out CorDebugDebugEventKind pDebugEventKind);

    [PreserveSig]
    int TryGetThread(out ICorDebugThread ppThread);
}