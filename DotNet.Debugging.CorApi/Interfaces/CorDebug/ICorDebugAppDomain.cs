using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugappdomain-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("3D6F5F63-7538-11D3-8D5B-00104B35E7EF")]
public partial interface ICorDebugAppDomain : ICorDebugController {
    [PreserveSig]
    int TryGetProcess(out ICorDebugProcess ppProcess);

    [PreserveSig]
    int TryEnumerateAssemblies(out ICorDebugAssemblyEnum ppAssemblies);

    [PreserveSig]
    int TryGetModuleFromMetaDataInterface(nint pIMetaData, out ICorDebugModule ppModule);

    [PreserveSig]
    int TryEnumerateBreakpoints(out ICorDebugBreakpointEnum ppBreakpoints);

    [PreserveSig]
    int TryEnumerateSteppers(out ICorDebugStepperEnum ppSteppers);

    [PreserveSig]
    int TryIsAttached([MarshalAs(UnmanagedType.Bool)] out bool pbAttached);

    [PreserveSig]
    int TryGetName(uint cchName, out uint pcchName, [Out][MarshalUsing(CountElementName = "cchName")] char[]? szName);

    [PreserveSig]
    int TryGetObject(out ICorDebugValue ppObject);

    [PreserveSig]
    int TryAttach();

    [PreserveSig]
    int TryGetID(out uint pId);

}