using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("4DCD6FB9-3CF0-43F0-9EDF-E833070FE644")]
public partial interface ICorDebugProcess12 {
    [PreserveSig]
    int TryGetAsyncStack(CordbAddress continuationAddress, out ICorDebugStackWalk ppStackWalk);
}