using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugfunction2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("EF0C490B-94C3-4E4D-B629-DDC134C532D8")]
public partial interface ICorDebugFunction2 {
    [PreserveSig]
    int TrySetJMCStatus([MarshalAs(UnmanagedType.Bool)] bool bIsJustMyCode);

    [PreserveSig]
    int TryGetJMCStatus([MarshalAs(UnmanagedType.Bool)] out bool pbIsJustMyCode);

    [PreserveSig]
    int TryEnumerateNativeCode(out ICorDebugCodeEnum ppCodeEnum);

    [PreserveSig]
    int TryGetVersionNumber(out uint pnVersion);
}