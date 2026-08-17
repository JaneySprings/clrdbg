using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugclass-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAF5-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugClass {
    [PreserveSig]
    int TryGetModule(out ICorDebugModule pModule);

    [PreserveSig]
    int TryGetToken(out TypeDefToken pTypeDef);

    [PreserveSig]
    int TryGetStaticFieldValue(FieldDefToken fieldDef, ICorDebugFrame pFrame, out ICorDebugValue ppValue);
}