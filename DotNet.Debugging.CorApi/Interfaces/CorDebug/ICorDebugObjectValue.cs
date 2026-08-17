using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugobjectvalue-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("18AD3D6E-B7D2-11D2-BD04-0000F80849BD")]
public partial interface ICorDebugObjectValue : ICorDebugValue {
    [PreserveSig]
    int TryGetClass(out ICorDebugClass ppClass);

    [PreserveSig]
    int TryGetFieldValue(ICorDebugClass pClass, FieldDefToken fieldDef, out ICorDebugValue ppValue);

    [PreserveSig]
    int TryGetVirtualMethod(MemberRefToken memberRef, out ICorDebugFunction ppFunction);

    [PreserveSig]
    int TryGetContext(out ICorDebugContext ppContext);

    [PreserveSig]
    int TryIsValueClass([MarshalAs(UnmanagedType.Bool)] out bool pbIsValueClass);

    [PreserveSig]
    int TryGetManagedCopy(out nint ppObject);

    [PreserveSig]
    int TrySetFromManagedCopy(nint pObject);

}