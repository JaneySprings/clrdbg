using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebug-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("3D6F5F61-7538-11D3-8D5B-00104B35E7EF")]
public partial interface ICorDebug {
    [PreserveSig]
    int TryInitialize();

    [PreserveSig]
    int TryTerminate();

    [PreserveSig]
    int TrySetManagedHandler(ICorDebugManagedCallback pCallback);

    [PreserveSig]
    int TrySetUnmanagedHandler(ICorDebugUnmanagedCallback pCallback);

    [PreserveSig]
    int TryCreateProcess(string lpApplicationName, string lpCommandLine, nint lpProcessAttributes, nint lpThreadAttributes, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags, nint lpEnvironment, string lpCurrentDirectory, nint lpStartupInfo, nint lpProcessInformation, CorDebugCreateProcessFlags debuggingFlags, out ICorDebugProcess ppProcess);

    [PreserveSig]
    int TryDebugActiveProcess(uint id, [MarshalAs(UnmanagedType.Bool)] bool win32Attach, out ICorDebugProcess ppProcess);

    [PreserveSig]
    int TryEnumerateProcesses(out ICorDebugProcessEnum ppProcess);

    [PreserveSig]
    int TryGetProcess(uint dwProcessId, out ICorDebugProcess ppProcess);

    [PreserveSig]
    int TryCanLaunchOrAttach(uint dwProcessId, [MarshalAs(UnmanagedType.Bool)] bool win32DebuggingEnabled);
}