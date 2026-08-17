using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugthread-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("938C6D66-7FB6-4F69-B389-425B8987329B")]
public partial interface ICorDebugThread {
    [PreserveSig]
    int TryGetProcess(out ICorDebugProcess ppProcess);

    [PreserveSig]
    int TryGetID(out uint pdwThreadId);

    [PreserveSig]
    int TryGetHandle(out nint phThreadHandle);

    [PreserveSig]
    int TryGetAppDomain(out ICorDebugAppDomain ppAppDomain);

    [PreserveSig]
    int TrySetDebugState(CorDebugThreadState state);

    [PreserveSig]
    int TryGetDebugState(out CorDebugThreadState pState);

    [PreserveSig]
    int TryGetUserState(out CorDebugUserState pState);

    [PreserveSig]
    int TryGetCurrentException(out ICorDebugValue ppExceptionObject);

    [PreserveSig]
    int TryClearCurrentException();

    [PreserveSig]
    int TryCreateStepper(out ICorDebugStepper ppStepper);

    [PreserveSig]
    int TryEnumerateChains(out ICorDebugChainEnum ppChains);

    [PreserveSig]
    int TryGetActiveChain(out ICorDebugChain ppChain);

    [PreserveSig]
    int TryGetActiveFrame(out ICorDebugFrame ppFrame);

    [PreserveSig]
    int TryGetRegisterSet(out ICorDebugRegisterSet ppRegisters);

    [PreserveSig]
    int TryCreateEval(out ICorDebugEval ppEval);

    [PreserveSig]
    int TryGetObject(out ICorDebugValue ppObject);
}