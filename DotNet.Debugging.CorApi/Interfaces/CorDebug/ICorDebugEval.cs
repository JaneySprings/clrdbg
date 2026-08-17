using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugeval-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("CC7BCAF6-8A68-11D2-983C-0000F808342D")]
public partial interface ICorDebugEval {
    [PreserveSig]
    int TryCallFunction(ICorDebugFunction pFunction, uint nArgs, [In][MarshalUsing(CountElementName = "nArgs")] ICorDebugValue[] ppArgs);

    [PreserveSig]
    int TryNewObject(ICorDebugFunction pConstructor, uint nArgs, [In][MarshalUsing(CountElementName = "nArgs")] ICorDebugValue[] ppArgs);

    [PreserveSig]
    int TryNewObjectNoConstructor(ICorDebugClass pClass);

    [PreserveSig]
    int TryNewString(string @string);

    [PreserveSig]
    int TryNewArray(CorElementType elementType, ICorDebugClass pElementClass, uint rank, [In][MarshalUsing(CountElementName = "rank")] uint[] dims, [In][MarshalUsing(CountElementName = "rank")] uint[] lowBounds);

    [PreserveSig]
    int TryIsActive([MarshalAs(UnmanagedType.Bool)] out bool pbActive);

    [PreserveSig]
    int TryAbort();

    [PreserveSig]
    int TryGetResult(out ICorDebugValue ppResult);

    [PreserveSig]
    int TryGetThread(out ICorDebugThread ppThread);

    [PreserveSig]
    int TryCreateValue(CorElementType elementType, ICorDebugClass? pElementClass, out ICorDebugValue ppValue);
}