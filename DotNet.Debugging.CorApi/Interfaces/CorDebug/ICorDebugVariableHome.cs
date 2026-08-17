using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugvariablehome-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("50847B8D-F43F-41B0-924C-6383A5F2278B")]
public partial interface ICorDebugVariableHome {
    [PreserveSig]
    int TryGetCode(out ICorDebugCode ppCode);

    [PreserveSig]
    int TryGetSlotIndex(out uint pSlotIndex);

    [PreserveSig]
    int TryGetArgumentIndex(out uint pArgumentIndex);

    [PreserveSig]
    int TryGetLiveRange(out uint pStartOffset, out uint pEndOffset);

    [PreserveSig]
    int TryGetLocationType(out VariableLocationType pLocationType);

    [PreserveSig]
    int TryGetRegister(out CorDebugRegister pRegister);

    [PreserveSig]
    int TryGetOffset(out int pOffset);
}