using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugeditandcontinueerrorinfo-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("8D600D41-F4F6-4CB3-B7EC-7BD164944036")]
public partial interface ICorDebugEditAndContinueErrorInfo {
    [PreserveSig]
    int TryGetModule(out ICorDebugModule ppModule);

    [PreserveSig]
    int TryGetToken(out MetadataToken pToken);

    [PreserveSig]
    int TryGetErrorCode(out int pHr);

    [PreserveSig]
    int TryGetString(uint cchString, out uint pcchString, [Out][MarshalUsing(CountElementName = "cchString")] char[]? szString);
}