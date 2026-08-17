using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("B35DD495-A555-463B-9BE9-C55338486BB8")]
public partial interface ICorDebugHeapValue4 {
    [PreserveSig]
    int TryCreatePinnedHandle(out ICorDebugHandleValue ppHandle);
}