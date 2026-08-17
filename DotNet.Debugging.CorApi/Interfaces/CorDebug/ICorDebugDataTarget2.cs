using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugdatatarget2-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("2EB364DA-605B-4E8D-B333-3394C4828D41")]
public partial interface ICorDebugDataTarget2 {
    [PreserveSig]
    int TryGetImageFromPointer(CordbAddress addr, out CordbAddress pImageBase, out uint pSize);

    [PreserveSig]
    int TryGetImageLocation(CordbAddress baseAddress, uint cchName, out uint pcchName, [Out][MarshalUsing(CountElementName = "cchName")] char[]? szName);

    [PreserveSig]
    int TryGetSymbolProviderForImage(CordbAddress imageBaseAddress, out ICorDebugSymbolProvider ppSymProvider);

    [PreserveSig]
    int TryEnumerateThreadIDs(uint cThreadIds, out uint pcThreadIds, [Out][MarshalUsing(CountElementName = "cThreadIds")] uint[] pThreadIds);

    [PreserveSig]
    int TryCreateVirtualUnwinder(uint nativeThreadID, uint contextFlags, uint cbContext, [In][MarshalUsing(CountElementName = "cbContext")] byte[] initialContext, out ICorDebugVirtualUnwinder ppUnwinder);
}