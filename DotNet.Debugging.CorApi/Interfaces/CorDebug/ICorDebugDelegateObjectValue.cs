using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("3AF70CC7-6047-47F6-A5C5-090A1A622638")]
public partial interface ICorDebugDelegateObjectValue {
    [PreserveSig]
    int TryGetTarget(out ICorDebugReferenceValue ppObject);

    [PreserveSig]
    int TryGetFunction(out ICorDebugFunction ppFunction);
}