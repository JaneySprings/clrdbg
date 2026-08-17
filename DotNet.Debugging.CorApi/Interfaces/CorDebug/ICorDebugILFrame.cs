using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugilframe-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("03E26311-4F76-11D3-88C6-006097945418")]
public partial interface ICorDebugILFrame : ICorDebugFrame {
    [PreserveSig]
    int TryGetIP(out uint pnOffset, out CorDebugMappingResult pMappingResult);

    [PreserveSig]
    int TrySetIP(uint nOffset);

    [PreserveSig]
    int TryEnumerateLocalVariables(out ICorDebugValueEnum ppValueEnum);

    [PreserveSig]
    int TryGetLocalVariable(uint dwIndex, out ICorDebugValue ppValue);

    [PreserveSig]
    int TryEnumerateArguments(out ICorDebugValueEnum ppValueEnum);

    [PreserveSig]
    int TryGetArgument(uint dwIndex, out ICorDebugValue ppValue);

    [PreserveSig]
    int TryGetStackDepth(out uint pDepth);

    [PreserveSig]
    int TryGetStackValue(uint dwIndex, out ICorDebugValue ppValue);

    [PreserveSig]
    int TryCanSetIP(uint nOffset);

}