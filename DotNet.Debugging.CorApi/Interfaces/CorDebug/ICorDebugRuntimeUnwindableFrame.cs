using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugruntimeunwindableframe-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("879CAC0A-4A53-4668-B8E3-CB8473CB187F")]
public partial interface ICorDebugRuntimeUnwindableFrame : ICorDebugFrame {
}