using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmutabledatatarget-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("A1B8A756-3CB6-4CCB-979F-3DF999673A59")]
public partial interface ICorDebugMutableDataTarget : ICorDebugDataTarget {
    [PreserveSig]
    int TryWriteVirtual(CordbAddress address, [In][MarshalUsing(CountElementName = "bytesRequested")] byte[] pBuffer, uint bytesRequested);

    [PreserveSig]
    int TrySetThreadContext(uint dwThreadID, uint contextSize, [In][MarshalUsing(CountElementName = "contextSize")] byte[] pContext);

    [PreserveSig]
    int TryContinueStatusChanged(uint dwThreadId, uint continueStatus);

}