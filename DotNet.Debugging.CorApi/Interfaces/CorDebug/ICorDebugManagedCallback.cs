using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmanagedcallback-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("3D6F5F60-7538-11D3-8D5B-00104B35E7EF")]
public partial interface ICorDebugManagedCallback {
    [PreserveSig]
    int TryBreakpoint(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugBreakpoint pBreakpoint);

    [PreserveSig]
    int TryStepComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugStepper pStepper, CorDebugStepReason reason);

    [PreserveSig]
    int TryBreak(ICorDebugAppDomain pAppDomain, ICorDebugThread thread);

    [PreserveSig]
    int TryException(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, [MarshalAs(UnmanagedType.Bool)] bool unhandled);

    [PreserveSig]
    int TryEvalComplete(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugEval pEval);

    [PreserveSig]
    int TryEvalException(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugEval pEval);

    [PreserveSig]
    int TryCreateProcess(ICorDebugProcess pProcess);

    [PreserveSig]
    int TryExitProcess(ICorDebugProcess pProcess);

    [PreserveSig]
    int TryCreateThread(ICorDebugAppDomain pAppDomain, ICorDebugThread thread);

    [PreserveSig]
    int TryExitThread(ICorDebugAppDomain pAppDomain, ICorDebugThread thread);

    [PreserveSig]
    int TryLoadModule(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule);

    [PreserveSig]
    int TryUnloadModule(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule);

    [PreserveSig]
    int TryLoadClass(ICorDebugAppDomain pAppDomain, ICorDebugClass c);

    [PreserveSig]
    int TryUnloadClass(ICorDebugAppDomain pAppDomain, ICorDebugClass c);

    [PreserveSig]
    int TryDebuggerError(ICorDebugProcess pProcess, int errorHR, uint errorCode);

    [PreserveSig]
    int TryLogMessage(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, int lLevel, string pLogSwitchName, string pMessage);

    [PreserveSig]
    int TryLogSwitch(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, int lLevel, uint ulReason, string pLogSwitchName, string pParentName);

    [PreserveSig]
    int TryCreateAppDomain(ICorDebugProcess pProcess, ICorDebugAppDomain pAppDomain);

    [PreserveSig]
    int TryExitAppDomain(ICorDebugProcess pProcess, ICorDebugAppDomain pAppDomain);

    [PreserveSig]
    int TryLoadAssembly(ICorDebugAppDomain pAppDomain, ICorDebugAssembly pAssembly);

    [PreserveSig]
    int TryUnloadAssembly(ICorDebugAppDomain pAppDomain, ICorDebugAssembly pAssembly);

    [PreserveSig]
    int TryControlCTrap(ICorDebugProcess pProcess);

    [PreserveSig]
    int TryNameChange(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread);

    [PreserveSig]
    int TryUpdateModuleSymbols(ICorDebugAppDomain pAppDomain, ICorDebugModule pModule, nint pSymbolStream);

    [PreserveSig]
    int TryEditAndContinueRemap(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugFunction pFunction, [MarshalAs(UnmanagedType.Bool)] bool fAccurate);

    [PreserveSig]
    int TryBreakpointSetError(ICorDebugAppDomain pAppDomain, ICorDebugThread pThread, ICorDebugBreakpoint pBreakpoint, uint dwError);
}