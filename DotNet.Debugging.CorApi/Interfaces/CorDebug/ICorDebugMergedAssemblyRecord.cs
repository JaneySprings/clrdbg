using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmergedassemblyrecord-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("FAA8637B-3BBE-4671-8E26-3B59875B922A")]
public partial interface ICorDebugMergedAssemblyRecord {
    [PreserveSig]
    int TryGetSimpleName(uint cchName, out uint pcchName, [Out][MarshalUsing(CountElementName = "cchName")] char[]? szName);

    [PreserveSig]
    int TryGetVersion(out ushort pMajor, out ushort pMinor, out ushort pBuild, out ushort pRevision);

    [PreserveSig]
    int TryGetCulture(uint cchCulture, out uint pcchCulture, [Out][MarshalUsing(CountElementName = "cchCulture")] char[]? szCulture);

    [PreserveSig]
    int TryGetPublicKey(uint cbPublicKey, out uint pcbPublicKey, [Out][MarshalUsing(CountElementName = "cbPublicKey")] byte[] pbPublicKey);

    [PreserveSig]
    int TryGetPublicKeyToken(uint cbPublicKeyToken, out uint pcbPublicKeyToken, [Out][MarshalUsing(CountElementName = "cbPublicKeyToken")] byte[] pbPublicKeyToken);

    [PreserveSig]
    int TryGetIndex(out uint pIndex);
}