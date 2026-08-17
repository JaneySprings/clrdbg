using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugfunctionbreakpoint-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAE9-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugFunctionBreakpoint : ICorDebugBreakpoint {
    [PreserveSig]
    int TryGetFunction(out ICorDebugFunction ppFunction);

    [PreserveSig]
    int TryGetOffset(out uint pnOffset);

}