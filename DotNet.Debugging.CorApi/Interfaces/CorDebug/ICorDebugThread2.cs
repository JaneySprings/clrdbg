using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugthread2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("2BD956D9-7B07-4BEF-8A98-12AA862417C5")]
public partial interface ICorDebugThread2 {
    [PreserveSig]
    int TryGetActiveFunctions(uint cFunctions, out uint pcFunctions, [In][Out][MarshalUsing(CountElementName = "cFunctions")] CorActiveFunction[]? pFunctions);

    [PreserveSig]
    int TryGetConnectionID(out uint pdwConnectionId);

    [PreserveSig]
    int TryGetTaskID(out ulong pTaskId);

    [PreserveSig]
    int TryGetVolatileOSThreadID(out uint pdwTid);

    [PreserveSig]
    int TryInterceptCurrentException(ICorDebugFrame pFrame);
}