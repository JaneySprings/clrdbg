using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugheapvalue3-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("A69ACAD8-2374-46E9-9FF8-B1F14120D296")]
public partial interface ICorDebugHeapValue3 {
    [PreserveSig]
    int TryGetThreadOwningMonitorLock(out ICorDebugThread ppThread, out uint pAcquisitionCount);

    [PreserveSig]
    int TryGetMonitorEventWaitList(out ICorDebugThreadEnum ppThreadEnum);
}