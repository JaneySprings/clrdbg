using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmda-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC726F2F-1DB7-459B-B0EC-05F01D841B42")]
public partial interface ICorDebugMDA {
    [PreserveSig]
    int TryGetName(uint cchName, out uint pcchName, [Out][MarshalUsing(CountElementName = "cchName")] char[]? szName);

    [PreserveSig]
    int TryGetDescription(uint cchName, out uint pcchName, [Out][MarshalUsing(CountElementName = "cchName")] char[]? szName);

    [PreserveSig]
    int TryGetXML(uint cchName, out uint pcchName, [Out][MarshalUsing(CountElementName = "cchName")] char[]? szName);

    [PreserveSig]
    int TryGetFlags(ref CorDebugMDAFlags pFlags);

    [PreserveSig]
    int TryGetOSThreadId(out uint pOsTid);
}