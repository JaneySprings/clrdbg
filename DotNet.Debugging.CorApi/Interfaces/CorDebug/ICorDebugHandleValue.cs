using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebughandlevalue-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("029596E8-276B-46A1-9821-732E96BBB00B")]
public partial interface ICorDebugHandleValue : ICorDebugReferenceValue {
    [PreserveSig]
    int TryGetHandleType(out CorDebugHandleType pType);

    [PreserveSig]
    int TryDispose();

}