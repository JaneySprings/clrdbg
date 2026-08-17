using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugheapvalue2-interface1
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("E3AC4D6C-9CB7-43E6-96CC-B21540E5083C")]
public partial interface ICorDebugHeapValue2 {
    [PreserveSig]
    int TryCreateHandle(CorDebugHandleType type, out ICorDebugHandleValue ppHandle);
}