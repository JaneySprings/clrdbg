using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("D332DB9E-B9B3-4125-8207-A14884F53216")]
public partial interface ICLRMetaHost {
    [PreserveSig]
    int TryGetRuntime(string pwzVersion, ref Guid riid, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<object>))] out object? ppRuntime);

    [PreserveSig]
    int TryGetVersionFromFile(string pwzFilePath, [Out][MarshalUsing(CountElementName = "pcchBuffer")] string[] pwzBuffer, ref uint pcchBuffer);

    [PreserveSig]
    int TryEnumerateInstalledRuntimes(out nint ppEnumerator);

    [PreserveSig]
    int TryEnumerateLoadedRuntimes(nint hndProcess, out nint ppEnumerator);

    [PreserveSig]
    int TryRequestRuntimeLoadedNotification(nint pCallbackFunction);

    [PreserveSig]
    int TryQueryLegacyV2RuntimeBinding(ref Guid riid, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<object>))] out object? ppUnk);

    [PreserveSig]
    int TryExitProcess(int iExitCode);
}