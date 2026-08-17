using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugregisterset-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCB0B-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugRegisterSet {
    [PreserveSig]
    int TryGetRegistersAvailable(out ulong pAvailable);

    [PreserveSig]
    int TryGetRegisters(ulong mask, uint regCount, [Out][MarshalUsing(CountElementName = "regCount")] ulong[] regBuffer);

    [PreserveSig]
    int TrySetRegisters(ulong mask, uint regCount, [In][MarshalUsing(CountElementName = "regCount")] ulong[] regBuffer);

    [PreserveSig]
    int TryGetThreadContext(uint contextSize, [In][Out][MarshalUsing(CountElementName = "contextSize")] byte[] context);

    [PreserveSig]
    int TrySetThreadContext(uint contextSize, [In][MarshalUsing(CountElementName = "contextSize")] byte[] context);
}