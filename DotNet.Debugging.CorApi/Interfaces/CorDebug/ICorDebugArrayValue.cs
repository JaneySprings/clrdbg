using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugarrayvalue-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("0405B0DF-A660-11D2-BD02-0000F80849BD")]
public partial interface ICorDebugArrayValue : ICorDebugHeapValue {
    [PreserveSig]
    int TryGetElementType(out CorElementType pType);

    [PreserveSig]
    int TryGetRank(out uint pnRank);

    [PreserveSig]
    int TryGetCount(out uint pnCount);

    [PreserveSig]
    int TryGetDimensions(uint cdim, [Out][MarshalUsing(CountElementName = "cdim")] uint[] dims);

    [PreserveSig]
    int TryHasBaseIndicies([MarshalAs(UnmanagedType.Bool)] out bool pbHasBaseIndicies);

    [PreserveSig]
    int TryGetBaseIndicies(uint cdim, [Out][MarshalUsing(CountElementName = "cdim")] uint[] indices);

    [PreserveSig]
    int TryGetElement(uint cdim, [In][MarshalUsing(CountElementName = "cdim")] uint[] indices, out ICorDebugValue ppValue);

    [PreserveSig]
    int TryGetElementAtPosition(uint nPosition, out ICorDebugValue ppValue);

}