using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugstepper2-interface1
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("C5B6E9C3-E7D1-4A8E-873B-7F047F0706F7")]
public partial interface ICorDebugStepper2 {
    [PreserveSig]
    int TrySetJMC([MarshalAs(UnmanagedType.Bool)] bool fIsJMCStepper);
}