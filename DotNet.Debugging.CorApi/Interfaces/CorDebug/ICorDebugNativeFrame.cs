using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugnativeframe-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("03E26314-4F76-11D3-88C6-006097945418")]
public partial interface ICorDebugNativeFrame : ICorDebugFrame {
    [PreserveSig]
    int TryGetIP(out uint pnOffset);

    [PreserveSig]
    int TrySetIP(uint nOffset);

    [PreserveSig]
    int TryGetRegisterSet(out ICorDebugRegisterSet ppRegisters);

    [PreserveSig]
    int TryGetLocalRegisterValue(CorDebugRegister reg, uint cbSigBlob, nuint pvSigBlob, out ICorDebugValue ppValue);

    [PreserveSig]
    int TryGetLocalDoubleRegisterValue(CorDebugRegister highWordReg, CorDebugRegister lowWordReg, uint cbSigBlob, nuint pvSigBlob, out ICorDebugValue ppValue);

    [PreserveSig]
    int TryGetLocalMemoryValue(CordbAddress address, uint cbSigBlob, nuint pvSigBlob, out ICorDebugValue ppValue);

    [PreserveSig]
    int TryGetLocalRegisterMemoryValue(CorDebugRegister highWordReg, CordbAddress lowWordAddress, uint cbSigBlob, nuint pvSigBlob, out ICorDebugValue ppValue);

    [PreserveSig]
    int TryGetLocalMemoryRegisterValue(CordbAddress highWordAddress, CorDebugRegister lowWordRegister, uint cbSigBlob, nuint pvSigBlob, out ICorDebugValue ppValue);

    [PreserveSig]
    int TryCanSetIP(uint nOffset);

}