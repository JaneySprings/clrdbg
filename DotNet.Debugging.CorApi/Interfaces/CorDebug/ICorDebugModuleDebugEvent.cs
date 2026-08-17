using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmoduledebugevent-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("51A15E8D-9FFF-4864-9B87-F4FBDEA747A2")]
public partial interface ICorDebugModuleDebugEvent : ICorDebugDebugEvent {
    [PreserveSig]
    int TryGetModule(out ICorDebugModule ppModule);

}