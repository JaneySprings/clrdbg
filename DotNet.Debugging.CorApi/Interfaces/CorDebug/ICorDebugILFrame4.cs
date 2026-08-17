using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugilframe4-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("AD914A30-C6D1-4AC5-9C5E-577F3BAA8A45")]
public partial interface ICorDebugILFrame4 {
    [PreserveSig]
    int TryEnumerateLocalVariablesEx(ILCodeKind flags, out ICorDebugValueEnum ppValueEnum);

    [PreserveSig]
    int TryGetLocalVariableEx(ILCodeKind flags, uint dwIndex, out ICorDebugValue ppValue);

    [PreserveSig]
    int TryGetCodeEx(ILCodeKind flags, out ICorDebugCode ppCode);
}