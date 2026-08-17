using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugframe-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAEF-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugFrame {
    [PreserveSig]
    int TryGetChain(out ICorDebugChain ppChain);

    [PreserveSig]
    int TryGetCode(out ICorDebugCode ppCode);

    [PreserveSig]
    int TryGetFunction(out ICorDebugFunction ppFunction);

    [PreserveSig]
    int TryGetFunctionToken(out MethodDefToken pToken);

    [PreserveSig]
    int TryGetStackRange(out CordbAddress pStart, out CordbAddress pEnd);

    [PreserveSig]
    int TryGetCaller(out ICorDebugFrame ppFrame);

    [PreserveSig]
    int TryGetCallee(out ICorDebugFrame ppFrame);

    [PreserveSig]
    int TryCreateStepper(out ICorDebugStepper ppStepper);
}