using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugcomobjectvalue-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("5F69C5E5-3E12-42DF-B371-F9D761D6EE24")]
public partial interface ICorDebugComObjectValue {
    [PreserveSig]
    int TryGetCachedInterfaceTypes([MarshalAs(UnmanagedType.Bool)] bool bIInspectableOnly, out ICorDebugTypeEnum ppInterfacesEnum);

    [PreserveSig]
    int TryGetCachedInterfacePointers([MarshalAs(UnmanagedType.Bool)] bool bIInspectableOnly, uint celt, out uint pcEltFetched, [Out][MarshalUsing(CountElementName = "celt")] CordbAddress[]? ptrs);
}