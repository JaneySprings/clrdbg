using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugappdomain3-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("8CB96A16-B588-42E2-B71C-DD849FC2ECCC")]
public partial interface ICorDebugAppDomain3 {
    [PreserveSig]
    int TryGetCachedWinRTTypesForIIDs(uint cReqTypes, [In][MarshalUsing(CountElementName = "cReqTypes")] Guid[] iidsToResolve, out ICorDebugTypeEnum ppTypesEnum);

    [PreserveSig]
    int TryGetCachedWinRTTypes(out ICorDebugGuidToTypeEnum ppGuidToTypeEnum);
}