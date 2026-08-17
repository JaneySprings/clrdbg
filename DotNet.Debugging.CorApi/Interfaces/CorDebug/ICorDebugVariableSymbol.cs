using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugvariablesymbol-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("707E8932-1163-48D9-8A93-F5B1F480FBB7")]
public partial interface ICorDebugVariableSymbol {
    [PreserveSig]
    int TryGetName(uint cchName, out uint pcchName, [Out][MarshalUsing(CountElementName = "cchName")] char[]? szName);

    [PreserveSig]
    int TryGetSize(out uint pcbValue);

    [PreserveSig]
    int TryGetValue(uint offset, uint cbContext, [In][MarshalUsing(CountElementName = "cbContext")] byte[] context, uint cbValue, out uint pcbValue, [Out][MarshalUsing(CountElementName = "cbValue")] byte[] pValue);

    [PreserveSig]
    int TrySetValue(uint offset, uint threadID, uint cbContext, [In][MarshalUsing(CountElementName = "cbContext")] byte[] context, uint cbValue, [In][MarshalUsing(CountElementName = "cbValue")] byte[] pValue);

    [PreserveSig]
    int TryGetSlotIndex(out uint pSlotIndex);
}