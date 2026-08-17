using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("3151C08D-4D09-4F9B-8838-2880BF18FE51")]
public partial interface ICLRDebuggingLibraryProvider {
    [PreserveSig]
    int TryProvideLibrary(string pwszFileName, uint dwTimestamp, uint dwSizeOfImage, out nint phModule);
}