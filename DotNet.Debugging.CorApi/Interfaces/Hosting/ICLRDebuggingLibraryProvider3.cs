using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/iclrdebugginglibraryprovider3-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("DE3AAB18-46A0-48B4-BF0D-2C336E69EA1B")]
public partial interface ICLRDebuggingLibraryProvider3 {
    [PreserveSig]
    int TryProvideWindowsLibrary(string pwszFileName, string pwszRuntimeModule, LibraryProviderIndexType indexType, uint dwTimestamp, uint dwSizeOfImage, out nint ppResolvedModulePath);

    [PreserveSig]
    int TryProvideUnixLibrary(string pwszFileName, string pwszRuntimeModule, LibraryProviderIndexType indexType, ref byte pbBuildId, int iBuildIdSize, out nint ppResolvedModulePath);
}