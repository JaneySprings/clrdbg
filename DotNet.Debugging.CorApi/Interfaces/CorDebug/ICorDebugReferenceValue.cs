using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugreferencevalue-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAF9-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugReferenceValue : ICorDebugValue {
    [PreserveSig]
    int TryIsNull([MarshalAs(UnmanagedType.Bool)] out bool pbNull);

    [PreserveSig]
    int TryGetValue(out CordbAddress pValue);

    [PreserveSig]
    int TrySetValue(CordbAddress value);

    [PreserveSig]
    int TryDereference(out ICorDebugValue ppValue);

    [PreserveSig]
    int TryDereferenceStrong(out ICorDebugValue ppValue);

}