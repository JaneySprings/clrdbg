using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugdatatarget-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("FE06DC28-49FB-4636-A4A3-E80DB4AE116C")]
public partial interface ICorDebugDataTarget {
    [PreserveSig]
    int TryGetPlatform(out CorDebugPlatform pTargetPlatform);

    [PreserveSig]
    int TryReadVirtual(CordbAddress address, [Out][MarshalUsing(CountElementName = "bytesRequested")] byte[] pBuffer, uint bytesRequested, out uint pBytesRead);

    [PreserveSig]
    int TryGetThreadContext(uint dwThreadID, uint contextFlags, uint contextSize, [Out][MarshalUsing(CountElementName = "contextSize")] byte[] pContext);
}