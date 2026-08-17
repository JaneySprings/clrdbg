using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugfunction-interface1
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAF3-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugFunction {
    [PreserveSig]
    int TryGetModule(out ICorDebugModule ppModule);

    [PreserveSig]
    int TryGetClass(out ICorDebugClass ppClass);

    [PreserveSig]
    int TryGetToken(out MethodDefToken pMethodDef);

    [PreserveSig]
    int TryGetILCode(out ICorDebugCode ppCode);

    [PreserveSig]
    int TryGetNativeCode(out ICorDebugCode ppCode);

    [PreserveSig]
    int TryCreateBreakpoint(out ICorDebugFunctionBreakpoint ppBreakpoint);

    [PreserveSig]
    int TryGetLocalVarSigToken(out SignatureToken pmdSig);

    [PreserveSig]
    int TryGetCurrentVersionNumber(out uint pnCurrentVersion);
}