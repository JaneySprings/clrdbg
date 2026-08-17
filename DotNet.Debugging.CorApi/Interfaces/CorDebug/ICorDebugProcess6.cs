using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugprocess6-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("11588775-7205-4CEB-A41A-93753C3153E9")]
public partial interface ICorDebugProcess6 {
    [PreserveSig]
    int TryDecodeEvent([In][MarshalUsing(CountElementName = "countBytes")] byte[] pRecord, uint countBytes, CorDebugRecordFormat format, uint dwFlags, uint dwThreadId, out ICorDebugDebugEvent ppEvent);

    [PreserveSig]
    int TryProcessStateChanged(CorDebugStateChange change);

    [PreserveSig]
    int TryGetCode(CordbAddress codeAddress, out ICorDebugCode ppCode);

    [PreserveSig]
    int TryEnableVirtualModuleSplitting([MarshalAs(UnmanagedType.Bool)] bool enableSplitting);

    [PreserveSig]
    int TryMarkDebuggerAttached([MarshalAs(UnmanagedType.Bool)] bool fIsAttached);

    [PreserveSig]
    int TryGetExportStepInfo(string pszExportName, out CorDebugCodeInvokeKind pInvokeKind, out CorDebugCodeInvokePurpose pInvokePurpose);
}