using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("F98421C4-E506-4D24-916F-0237EE853EC6")]
public partial interface ICorDebugThread5 {
    [PreserveSig]
    int TryGetBytesAllocated(out ulong pSohAllocatedBytes, out ulong pUohAllocatedBytes);
}