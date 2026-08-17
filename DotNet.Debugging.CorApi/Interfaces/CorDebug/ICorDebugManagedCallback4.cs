using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("322911AE-16A5-49BA-84A3-ED69678138A3")]
public partial interface ICorDebugManagedCallback4 {
    [PreserveSig]
    int TryBeforeGarbageCollection(ICorDebugProcess pProcess);

    [PreserveSig]
    int TryAfterGarbageCollection(ICorDebugProcess pProcess);

    [PreserveSig]
    int TryDataBreakpoint(ICorDebugProcess pProcess, ICorDebugThread pThread, ref byte pContext, uint contextSize);
}