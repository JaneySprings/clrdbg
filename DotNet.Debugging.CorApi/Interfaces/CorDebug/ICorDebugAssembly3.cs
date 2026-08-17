using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugassembly3-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("76361AB2-8C86-4FE9-96F2-F73D8843570A")]
public partial interface ICorDebugAssembly3 {
    [PreserveSig]
    int TryGetContainerAssembly(out ICorDebugAssembly ppAssembly);

    [PreserveSig]
    int TryEnumerateContainedAssemblies(out ICorDebugAssemblyEnum ppAssemblies);
}