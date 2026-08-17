using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebuginstancefieldsymbol-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("A074096B-3ADC-4485-81DA-68C7A4EA52DB")]
public partial interface ICorDebugInstanceFieldSymbol {
    [PreserveSig]
    int TryGetName(uint cchName, out uint pcchName, [Out][MarshalUsing(CountElementName = "cchName")] char[]? szName);

    [PreserveSig]
    int TryGetSize(out uint pcbSize);

    [PreserveSig]
    int TryGetOffset(out uint pcbOffset);
}