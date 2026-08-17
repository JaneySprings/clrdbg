using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugboxvalue-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAFC-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugBoxValue : ICorDebugHeapValue {
    [PreserveSig]
    int TryGetObject(out ICorDebugObjectValue ppObject);

}