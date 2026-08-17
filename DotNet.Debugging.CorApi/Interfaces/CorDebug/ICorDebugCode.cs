using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugcode-interface1
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAF4-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugCode {
    [PreserveSig]
    int TryIsIL([MarshalAs(UnmanagedType.Bool)] out bool pbIL);

    [PreserveSig]
    int TryGetFunction(out ICorDebugFunction ppFunction);

    [PreserveSig]
    int TryGetAddress(out CordbAddress pStart);

    [PreserveSig]
    int TryGetSize(out uint pcBytes);

    [PreserveSig]
    int TryCreateBreakpoint(uint offset, out ICorDebugFunctionBreakpoint ppBreakpoint);

    [PreserveSig]
    int TryGetCode(uint startOffset, uint endOffset, uint cBufferAlloc, [Out][MarshalUsing(CountElementName = "cBufferAlloc")] byte[] buffer, out uint pcBufferSize);

    [PreserveSig]
    int TryGetVersionNumber(out uint nVersion);

    [PreserveSig]
    int TryGetILToNativeMapping(uint cMap, out uint pcMap, [Out][MarshalUsing(CountElementName = "cMap")] CorDebugIlToNativeMap[]? map);

    [PreserveSig]
    int TryGetEnCRemapSequencePoints(uint cMap, out uint pcMap, [Out][MarshalUsing(CountElementName = "cMap")] uint[] offsets);
}