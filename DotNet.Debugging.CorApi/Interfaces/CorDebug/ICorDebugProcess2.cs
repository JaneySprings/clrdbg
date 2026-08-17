using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugprocess2-interface1
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("AD1B3588-0EF0-4744-A496-AA09A9F80371")]
public partial interface ICorDebugProcess2 {
    [PreserveSig]
    int TryGetThreadForTaskID(ulong taskid, out ICorDebugThread2 ppThread);

    [PreserveSig]
    int TryGetVersion(out CorVersion version);

    [PreserveSig]
    int TrySetUnmanagedBreakpoint(CordbAddress address, uint bufsize, [Out][MarshalUsing(CountElementName = "bufsize")] byte[] buffer, out uint bufLen);

    [PreserveSig]
    int TryClearUnmanagedBreakpoint(CordbAddress address);

    [PreserveSig]
    int TrySetDesiredNGENCompilerFlags(uint pdwFlags);

    [PreserveSig]
    int TryGetDesiredNGENCompilerFlags(out uint pdwFlags);

    [PreserveSig]
    int TryGetReferenceValueFromGCHandle(nuint handle, out ICorDebugReferenceValue pOutValue);
}