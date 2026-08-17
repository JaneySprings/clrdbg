using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmemorybuffer-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("677888B3-D160-4B8C-A73B-D79E6AAA1D13")]
public partial interface ICorDebugMemoryBuffer {
    [PreserveSig]
    int TryGetStartAddress(out nint address);

    [PreserveSig]
    int TryGetSize(out uint pcbBufferLength);
}