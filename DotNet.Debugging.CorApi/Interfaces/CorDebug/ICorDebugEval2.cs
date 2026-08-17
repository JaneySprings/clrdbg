using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugeval2-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("FB0D9CE7-BE66-4683-9D32-A42A04E2FD91")]
public partial interface ICorDebugEval2 {
    [PreserveSig]
    int TryCallParameterizedFunction(ICorDebugFunction pFunction, uint nTypeArgs, [In][MarshalUsing(CountElementName = "nTypeArgs")] ICorDebugType[]? ppTypeArgs, uint nArgs, [In][MarshalUsing(CountElementName = "nArgs")] ICorDebugValue[] ppArgs);

    [PreserveSig]
    int TryCreateValueForType(ICorDebugType pType, out ICorDebugValue ppValue);

    [PreserveSig]
    int TryNewParameterizedObject(ICorDebugFunction pConstructor, uint nTypeArgs, [In][MarshalUsing(CountElementName = "nTypeArgs")] ICorDebugType[]? ppTypeArgs, uint nArgs, [In][MarshalUsing(CountElementName = "nArgs")] ICorDebugValue[] ppArgs);

    [PreserveSig]
    int TryNewParameterizedObjectNoConstructor(ICorDebugClass pClass, uint nTypeArgs, [In][MarshalUsing(CountElementName = "nTypeArgs")] ICorDebugType[]? ppTypeArgs);

    [PreserveSig]
    int TryNewParameterizedArray(ICorDebugType pElementType, uint rank, [In][MarshalUsing(CountElementName = "rank")] uint[] dims, [In][MarshalUsing(CountElementName = "rank")] uint[] lowBounds);

    [PreserveSig]
    int TryNewStringWithLength(string @string, uint uiLength);

    [PreserveSig]
    int TryRudeAbort();
}