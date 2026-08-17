using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugprocess8-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("2E6F28C1-85EB-4141-80AD-0A90944B9639")]
public partial interface ICorDebugProcess8 {
    [PreserveSig]
    int TryEnableExceptionCallbacksOutsideOfMyCode([MarshalAs(UnmanagedType.Bool)] bool enableExceptionsOutsideOfJMC);
}