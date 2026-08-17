using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmodule2-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("7FCC5FB5-49C0-41DE-9938-3B88B5B9ADD7")]
public partial interface ICorDebugModule2 {
    [PreserveSig]
    int TrySetJMCStatus([MarshalAs(UnmanagedType.Bool)] bool bIsJustMyCode, uint cTokens, [In][MarshalUsing(CountElementName = "cTokens")] MetadataToken[] pTokens);

    [PreserveSig]
    int TryApplyChanges(uint cbMetadata, [In][MarshalUsing(CountElementName = "cbMetadata")] byte[] pbMetadata, uint cbIL, [In][MarshalUsing(CountElementName = "cbIL")] byte[] pbIL);

    [PreserveSig]
    int TrySetJITCompilerFlags(CorDebugJITCompilerFlags dwFlags);

    [PreserveSig]
    int TryGetJITCompilerFlags(out CorDebugJITCompilerFlags pdwFlags);

    [PreserveSig]
    int TryResolveAssembly(MetadataToken tkAssemblyRef, out ICorDebugAssembly ppAssembly);
}