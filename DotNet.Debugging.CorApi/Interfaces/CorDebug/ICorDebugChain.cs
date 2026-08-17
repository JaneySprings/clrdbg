using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugchain-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAEE-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugChain {
    [PreserveSig]
    int TryGetThread(out ICorDebugThread ppThread);

    [PreserveSig]
    int TryGetStackRange(out CordbAddress pStart, out CordbAddress pEnd);

    [PreserveSig]
    int TryGetContext(out ICorDebugContext ppContext);

    [PreserveSig]
    int TryGetCaller(out ICorDebugChain ppChain);

    [PreserveSig]
    int TryGetCallee(out ICorDebugChain ppChain);

    [PreserveSig]
    int TryGetPrevious(out ICorDebugChain ppChain);

    [PreserveSig]
    int TryGetNext(out ICorDebugChain ppChain);

    [PreserveSig]
    int TryIsManaged([MarshalAs(UnmanagedType.Bool)] out bool pManaged);

    [PreserveSig]
    int TryEnumerateFrames(out ICorDebugFrameEnum ppFrames);

    [PreserveSig]
    int TryGetActiveFrame(out ICorDebugFrame ppFrame);

    [PreserveSig]
    int TryGetRegisterSet(out ICorDebugRegisterSet ppRegisters);

    [PreserveSig]
    int TryGetReason(out CorDebugChainReason pReason);
}