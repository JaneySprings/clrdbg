using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugassembly-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("DF59507C-D47A-459E-BCE2-6427EAC8FD06")]
public partial interface ICorDebugAssembly {
    [PreserveSig]
    int TryGetProcess(out ICorDebugProcess ppProcess);

    [PreserveSig]
    int TryGetAppDomain(out ICorDebugAppDomain ppAppDomain);

    [PreserveSig]
    int TryEnumerateModules(out ICorDebugModuleEnum ppModules);

    [PreserveSig]
    int TryGetCodeBase(uint cchName, out uint pcchName, [Out][MarshalUsing(CountElementName = "cchName")] char[]? szName);

    [PreserveSig]
    int TryGetName(uint cchName, out uint pcchName, [Out][MarshalUsing(CountElementName = "cchName")] char[]? szName);
}