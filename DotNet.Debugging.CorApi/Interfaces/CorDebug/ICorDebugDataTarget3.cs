using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugdatatarget3-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("D05E60C3-848C-4E7D-894E-623320FF6AFA")]
public partial interface ICorDebugDataTarget3 {
    [PreserveSig]
    int TryGetLoadedModules(uint cRequestedModules, out uint pcFetchedModules, [Out][MarshalUsing(CountElementName = "cRequestedModules")] ICorDebugLoadedModule[] pLoadedModules);
}