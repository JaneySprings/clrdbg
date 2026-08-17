using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmetadatalocator-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("7CEF8BA9-2EF7-42BF-973F-4171474F87D9")]
public partial interface ICorDebugMetaDataLocator {
    [PreserveSig]
    int TryGetMetaData(string wszImagePath, uint dwImageTimeStamp, uint dwImageSize, uint cchPathBuffer, out uint pcchPathBuffer, [Out][MarshalUsing(CountElementName = "cchPathBuffer")] char[] wszPathBuffer);
}