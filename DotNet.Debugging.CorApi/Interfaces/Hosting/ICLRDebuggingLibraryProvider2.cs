using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/iclrdebugginglibraryprovider2-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("E04E2FF1-DCFD-45D5-BCD1-16FFF2FAF7BA")]
public partial interface ICLRDebuggingLibraryProvider2 {
    [PreserveSig]
    int TryProvideLibrary2(string pwszFileName, uint dwTimestamp, uint dwSizeOfImage, out nint ppResolvedModulePath);
}