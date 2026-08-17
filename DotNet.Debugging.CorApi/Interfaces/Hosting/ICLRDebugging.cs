using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("D28F3C5A-9634-4206-A509-477552EEFB10")]
public partial interface ICLRDebugging {
    [PreserveSig]
    int TryOpenVirtualProcess(ulong moduleBaseAddress, nint pDataTarget, ICLRDebuggingLibraryProvider pLibraryProvider, ref ClrDebuggingVersion pMaxDebuggerSupportedVersion, ref Guid riidProcess, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<object>))] out object? ppProcess, ref ClrDebuggingVersion pVersion, out ClrDebuggingProcessFlags pdwFlags);

    [PreserveSig]
    int TryCanUnloadNow(nint hModule);
}