using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugstringvalue-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAFD-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugStringValue : ICorDebugHeapValue {
    [PreserveSig]
    int TryGetLength(out uint pcchString);

    [PreserveSig]
    int TryGetString(uint cchString, out uint pcchString, [Out][MarshalUsing(CountElementName = "cchString")] char[]? szString);

}