using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugilframe2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("5D88A994-6C30-479B-890F-BCEF88B129A5")]
public partial interface ICorDebugILFrame2 {
    [PreserveSig]
    int TryRemapFunction(uint newILOffset);

    [PreserveSig]
    int TryEnumerateTypeParameters(out ICorDebugTypeEnum ppTyParEnum);
}