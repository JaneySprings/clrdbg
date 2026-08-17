using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugprocess7-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("9B2C54E4-119F-4D6F-B402-527603266D69")]
public partial interface ICorDebugProcess7 {
    [PreserveSig]
    int TrySetWriteableMetadataUpdateMode(WriteableMetadataUpdateMode flags);
}