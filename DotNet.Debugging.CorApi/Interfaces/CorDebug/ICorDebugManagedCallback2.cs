using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback2-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("250E5EEA-DB5C-4C76-B6F3-8C46F12E3203")]
public partial interface ICorDebugManagedCallback2 {
    [PreserveSig]
    int TryFunctionRemapOpportunity(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pOldFunction, ICorDebugFunction pNewFunction, uint oldILOffset);

    [PreserveSig]
    int TryCreateConnection(ICorDebugProcess pProcess, uint dwConnectionId, string pConnName);

    [PreserveSig]
    int TryChangeConnection(ICorDebugProcess pProcess, uint dwConnectionId);

    [PreserveSig]
    int TryDestroyConnection(ICorDebugProcess pProcess, uint dwConnectionId);

    [PreserveSig]
    int TryException(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFrame pFrame, uint nOffset, CorDebugExceptionCallbackType dwEventType, uint dwFlags);

    [PreserveSig]
    int TryExceptionUnwind(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, CorDebugExceptionUnwindCallbackType dwEventType, uint dwFlags);

    [PreserveSig]
    int TryFunctionRemapComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pFunction);

    [PreserveSig]
    int TryMDANotification(ICorDebugController pController, ICorDebugThread pThread, ICorDebugMDA pMDA);
}