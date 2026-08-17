using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugsymbolprovider2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("F9801807-4764-4330-9E67-4F685094165E")]
public partial interface ICorDebugSymbolProvider2 {
    [PreserveSig]
    int TryGetGenericDictionaryInfo(out ICorDebugMemoryBuffer ppMemoryBuffer);

    [PreserveSig]
    int TryGetFrameProps(uint codeRva, out uint pCodeStartRva, out uint pParentFrameStartRva);
}