using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugregisterset2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("6DC7BA3F-89BA-4459-9EC1-9D60937B468D")]
public partial interface ICorDebugRegisterSet2 {
    [PreserveSig]
    int TryGetRegistersAvailable(uint numChunks, [Out][MarshalUsing(CountElementName = "numChunks")] byte[] availableRegChunks);

    [PreserveSig]
    int TryGetRegisters(uint maskCount, [In][MarshalUsing(CountElementName = "maskCount")] byte[] mask, uint regCount, [Out][MarshalUsing(CountElementName = "regCount")] ulong[] regBuffer);

    [PreserveSig]
    int TrySetRegisters(uint maskCount, [In][MarshalUsing(CountElementName = "maskCount")] byte[] mask, uint regCount, [In][MarshalUsing(CountElementName = "regCount")] ulong[] regBuffer);
}