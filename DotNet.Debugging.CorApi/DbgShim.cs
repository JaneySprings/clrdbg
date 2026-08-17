using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DotNet.Debugging.CorApi;

public static unsafe partial class DbgShim {
    [LibraryImport("dbgshim", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int CreateProcessForLaunch(string lpCommandLine, [MarshalAs(UnmanagedType.Bool)] bool bSuspendProcess, nint lpEnvironment, string? lpCurrentDirectory, out uint pProcessId, out nint pResumeHandle);

    [LibraryImport("dbgshim")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int ResumeProcess(nint hResumeHandle);

    [LibraryImport("dbgshim")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int CloseResumeHandle(nint hResumeHandle);

    [LibraryImport("dbgshim")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int RegisterForRuntimeStartup(uint dwProcessId, delegate* unmanaged[Cdecl]<void*, void*, int, void> pfnCallback, nint parameter, out nint pUnregisterToken);

    [LibraryImport("dbgshim", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int RegisterForRuntimeStartupEx(uint dwProcessId, string? szApplicationGroupId, delegate* unmanaged[Cdecl]<void*, void*, int, void> pfnCallback, nint parameter, out nint pUnregisterToken);

    [LibraryImport("dbgshim", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int RegisterForRuntimeStartup3(uint dwProcessId, string? szApplicationGroupId, ICLRDebuggingLibraryProvider3 pLibraryProvider, delegate* unmanaged[Cdecl]<void*, void*, int, void> pfnCallback, nint parameter, out nint pUnregisterToken);

    [LibraryImport("dbgshim")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int UnregisterForRuntimeStartup(nint pUnregisterToken);

    [LibraryImport("dbgshim")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int GetStartupNotificationEvent(uint debuggeePID, out nint phStartupEvent);

    [LibraryImport("dbgshim")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int EnumerateCLRs(uint debuggeePID, out nint ppHandleArrayOut, out nint ppStringArrayOut, out uint pdwArrayLengthOut);

    [LibraryImport("dbgshim")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int CloseCLREnumeration(nint pHandleArray, nint pStringArray, uint dwArrayLength);

    [LibraryImport("dbgshim", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int CreateVersionStringFromModule(uint pidDebuggee, string szModuleName, [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[] pBuffer, uint cchBuffer, out uint pdwLength);

    [LibraryImport("dbgshim", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int CreateDebuggingInterfaceFromVersion(string szDebuggeeVersion, out ICorDebug ppCordb);

    [LibraryImport("dbgshim", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int CreateDebuggingInterfaceFromVersionEx(int iDebuggerVersion, string szDebuggeeVersion, out ICorDebug ppCordb);

    [LibraryImport("dbgshim", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int CreateDebuggingInterfaceFromVersion2(int iDebuggerVersion, string szDebuggeeVersion, string? szApplicationGroupId, out ICorDebug ppCordb);

    [LibraryImport("dbgshim", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int CreateDebuggingInterfaceFromVersion3(int iDebuggerVersion, string szDebuggeeVersion, string? szApplicationGroupId, ICLRDebuggingLibraryProvider3 pLibraryProvider, out ICorDebug ppCordb);

    [LibraryImport("dbgshim", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(CallConvCdecl) })]
    public static partial int RegisterForRuntimeStartupRemotePort(string szIp, uint dwPort, string szPlatform, [MarshalAs(UnmanagedType.Bool)] bool bIsServer, string szMscordbiPath, string szAssemblyBasePath, out ICorDebug ppCordb);
}