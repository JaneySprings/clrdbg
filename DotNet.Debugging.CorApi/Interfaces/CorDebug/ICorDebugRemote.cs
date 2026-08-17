using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugremote-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("D5EBB8E2-7BBE-4C1D-98A6-A3C04CBDEF64")]
public partial interface ICorDebugRemote {
    [PreserveSig]
    int TryCreateProcessEx(ICorDebugRemoteTarget pRemoteTarget, string lpApplicationName, string lpCommandLine, nint lpProcessAttributes, nint lpThreadAttributes, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags, nint lpEnvironment, string lpCurrentDirectory, nint lpStartupInfo, nint lpProcessInformation, CorDebugCreateProcessFlags debuggingFlags, out ICorDebugProcess ppProcess);

    [PreserveSig]
    int TryDebugActiveProcessEx(ICorDebugRemoteTarget pRemoteTarget, uint dwProcessId, [MarshalAs(UnmanagedType.Bool)] bool fWin32Attach, out ICorDebugProcess ppProcess);
}