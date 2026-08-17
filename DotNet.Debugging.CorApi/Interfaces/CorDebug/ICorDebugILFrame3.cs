using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugilframe3-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("9A9E2ED6-04DF-4FE0-BB50-CAB64126AD24")]
public partial interface ICorDebugILFrame3 {
    [PreserveSig]
    int TryGetReturnValueForILOffset(uint ILoffset, out ICorDebugValue ppReturnValue);
}