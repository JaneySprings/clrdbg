using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugcontroller-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("3D6F5F62-7538-11D3-8D5B-00104B35E7EF")]
public partial interface ICorDebugController {
    [PreserveSig]
    int TryStop(uint dwTimeoutIgnored);

    [PreserveSig]
    int TryContinue([MarshalAs(UnmanagedType.Bool)] bool fIsOutOfBand);

    [PreserveSig]
    int TryIsRunning([MarshalAs(UnmanagedType.Bool)] out bool pbRunning);

    [PreserveSig]
    int TryHasQueuedCallbacks(ICorDebugThread pThread, [MarshalAs(UnmanagedType.Bool)] out bool pbQueued);

    [PreserveSig]
    int TryEnumerateThreads(out ICorDebugThreadEnum ppThreads);

    [PreserveSig]
    int TrySetAllThreadsDebugState(CorDebugThreadState state, ICorDebugThread pExceptThisThread);

    [PreserveSig]
    int TryDetach();

    [PreserveSig]
    int TryTerminate(uint exitCode);

    [PreserveSig]
    int TryCanCommitChanges(uint cSnapshots, [In][MarshalUsing(CountElementName = "cSnapshots")] ICorDebugEditAndContinueSnapshot[] pSnapshots, out ICorDebugErrorInfoEnum pError);

    [PreserveSig]
    int TryCommitChanges(uint cSnapshots, [In][MarshalUsing(CountElementName = "cSnapshots")] ICorDebugEditAndContinueSnapshot[] pSnapshots, out ICorDebugErrorInfoEnum pError);
}