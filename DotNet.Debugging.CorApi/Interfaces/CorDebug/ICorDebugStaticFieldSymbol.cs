using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugstaticfieldsymbol-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CBF9DA63-F68D-4BBB-A21C-15A45EAADF5B")]
public partial interface ICorDebugStaticFieldSymbol {
    [PreserveSig]
    int TryGetName(uint cchName, out uint pcchName, [Out][MarshalUsing(CountElementName = "cchName")] char[]? szName);

    [PreserveSig]
    int TryGetSize(out uint pcbSize);

    [PreserveSig]
    int TryGetAddress(out CordbAddress pRVA);
}