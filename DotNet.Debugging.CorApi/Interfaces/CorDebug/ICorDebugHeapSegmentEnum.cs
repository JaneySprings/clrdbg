using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugheapsegmentenum-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("A2FA0F8E-D045-11DF-AC8E-CE2ADFD72085")]
public partial interface ICorDebugHeapSegmentEnum : ICorDebugEnum {
    [PreserveSig]
    int TryNext(uint celt, [Out][MarshalUsing(CountElementName = "celt")] CorSegment[] segments, out uint pceltFetched);

}