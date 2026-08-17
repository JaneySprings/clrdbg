using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugremotetarget-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("C3ED8383-5A49-4CF5-B4B7-01864D9E582D")]
public partial interface ICorDebugRemoteTarget {
    [PreserveSig]
    int TryGetHostName(uint cchHostName, out uint pcchHostName, [Out][MarshalUsing(CountElementName = "cchHostName")] char[]? szHostName);
}