using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugprocess-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("3D6F5F64-7538-11D3-8D5B-00104B35E7EF")]
public partial interface ICorDebugProcess : ICorDebugController {
    [PreserveSig]
    int TryGetID(out uint pdwProcessId);

    [PreserveSig]
    int TryGetHandle(out nint phProcessHandle);

    [PreserveSig]
    int TryGetThread(uint dwThreadId, out ICorDebugThread ppThread);

    [PreserveSig]
    int TryEnumerateObjects(out ICorDebugObjectEnum ppObjects);

    [PreserveSig]
    int TryIsTransitionStub(CordbAddress address, [MarshalAs(UnmanagedType.Bool)] out bool pbTransitionStub);

    [PreserveSig]
    int TryIsOSSuspended(uint threadID, [MarshalAs(UnmanagedType.Bool)] out bool pbSuspended);

    [PreserveSig]
    int TryGetThreadContext(uint threadID, uint contextSize, [In][Out][MarshalUsing(CountElementName = "contextSize")] byte[] context);

    [PreserveSig]
    int TrySetThreadContext(uint threadID, uint contextSize, [In][MarshalUsing(CountElementName = "contextSize")] byte[] context);

    [PreserveSig]
    int TryReadMemory(CordbAddress address, uint size, [Out][MarshalUsing(CountElementName = "size")] byte[] buffer, out nuint read);

    [PreserveSig]
    int TryWriteMemory(CordbAddress address, uint size, [In][MarshalUsing(CountElementName = "size")] byte[] buffer, out nuint written);

    [PreserveSig]
    int TryClearCurrentException(uint threadID);

    [PreserveSig]
    int TryEnableLogMessages([MarshalAs(UnmanagedType.Bool)] bool fOnOff);

    [PreserveSig]
    int TryModifyLogSwitch(string pLogSwitchName, int lLevel);

    [PreserveSig]
    int TryEnumerateAppDomains(out ICorDebugAppDomainEnum ppAppDomains);

    [PreserveSig]
    int TryGetObject(out ICorDebugValue ppObject);

    [PreserveSig]
    int TryThreadForFiberCookie(uint fiberCookie, out ICorDebugThread ppThread);

    [PreserveSig]
    int TryGetHelperThreadID(out uint pThreadID);

}