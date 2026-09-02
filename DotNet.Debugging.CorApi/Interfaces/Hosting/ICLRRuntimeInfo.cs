using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("BD39D1D2-BA2F-486A-89B0-B4B0CB466891")]
public partial interface ICLRRuntimeInfo {
    [PreserveSig]
    int TryGetVersionString([Out][MarshalUsing(CountElementName = "pcchBuffer")] char[]? pwzBuffer, ref uint pcchBuffer);

    [PreserveSig]
    int TryGetRuntimeDirectory([Out][MarshalUsing(CountElementName = "pcchBuffer")] char[] pwzBuffer, ref uint pcchBuffer);

    [PreserveSig]
    int TryIsLoaded(nint hndProcess, [MarshalAs(UnmanagedType.Bool)] out bool pbLoaded);

    [PreserveSig]
    int TryLoadErrorString(uint iResourceID, [Out][MarshalUsing(CountElementName = "pcchBuffer")] char[] pwzBuffer, ref uint pcchBuffer, int iLocaleID);

    [PreserveSig]
    int TryLoadLibrary(string pwzDllName, out nint phndModule);

    [PreserveSig]
    int TryGetProcAddress([MarshalAs(UnmanagedType.LPStr)] string pszProcName, out nint ppProc);

    [PreserveSig]
    int TryGetInterface(ref Guid rclsid, ref Guid riid, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<object>))] out object? ppUnk);

    [PreserveSig]
    int TryIsLoadable([MarshalAs(UnmanagedType.Bool)] out bool pbLoadable);

    [PreserveSig]
    int TrySetDefaultStartupFlags(uint dwStartupFlags, string pwzHostConfigFile);

    [PreserveSig]
    int TryGetDefaultStartupFlags(out uint pdwStartupFlags, [Out][MarshalUsing(CountElementName = "pcchHostConfigFile")] char[]? pwzHostConfigFile, ref uint pcchHostConfigFile);

    [PreserveSig]
    int TryBindAsLegacyV2Runtime();

    [PreserveSig]
    int TryIsStarted([MarshalAs(UnmanagedType.Bool)] out bool pbStarted, out uint pdwStartupFlags);
}