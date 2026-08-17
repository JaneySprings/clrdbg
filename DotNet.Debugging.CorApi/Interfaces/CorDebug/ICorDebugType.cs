using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugtype-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("D613F0BB-ACE1-4C19-BD72-E4C08D5DA7F5")]
public partial interface ICorDebugType {
    [PreserveSig]
    int TryGetType(out CorElementType ty);

    [PreserveSig]
    int TryGetClass(out ICorDebugClass ppClass);

    [PreserveSig]
    int TryEnumerateTypeParameters(out ICorDebugTypeEnum ppTyParEnum);

    [PreserveSig]
    int TryGetFirstTypeParameter(out ICorDebugType value);

    [PreserveSig]
    int TryGetBase(out ICorDebugType pBase);

    [PreserveSig]
    int TryGetStaticFieldValue(FieldDefToken fieldDef, ICorDebugFrame pFrame, out ICorDebugValue ppValue);

    [PreserveSig]
    int TryGetRank(out uint pnRank);
}