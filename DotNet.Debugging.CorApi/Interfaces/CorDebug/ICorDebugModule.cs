using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugmodule-interface
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("DBA2D8C1-E5C5-4069-8C13-10A7C6ABF43D")]
public partial interface ICorDebugModule {
    [PreserveSig]
    int TryGetProcess(out ICorDebugProcess ppProcess);

    [PreserveSig]
    int TryGetBaseAddress(out CordbAddress pAddress);

    [PreserveSig]
    int TryGetAssembly(out ICorDebugAssembly ppAssembly);

    [PreserveSig]
    int TryGetName(uint cchName, out uint pcchName, [Out][MarshalUsing(CountElementName = "cchName")] char[]? szName);

    [PreserveSig]
    int TryEnableJITDebugging([MarshalAs(UnmanagedType.Bool)] bool bTrackJITInfo, [MarshalAs(UnmanagedType.Bool)] bool bAllowJitOpts);

    [PreserveSig]
    int TryEnableClassLoadCallbacks([MarshalAs(UnmanagedType.Bool)] bool bClassLoadCallbacks);

    [PreserveSig]
    int TryGetFunctionFromToken(MethodDefToken methodDef, out ICorDebugFunction ppFunction);

    [PreserveSig]
    int TryGetFunctionFromRVA(CordbAddress rva, out ICorDebugFunction ppFunction);

    [PreserveSig]
    int TryGetClassFromToken(TypeDefToken typeDef, out ICorDebugClass ppClass);

    [PreserveSig]
    int TryCreateBreakpoint(out ICorDebugModuleBreakpoint ppBreakpoint);

    [PreserveSig]
    int TryGetEditAndContinueSnapshot(out ICorDebugEditAndContinueSnapshot ppEditAndContinueSnapshot);

    [PreserveSig]
    int TryGetMetaDataInterface(ref Guid riid, [MarshalUsing(typeof(UniqueComInterfaceMarshaller<object>))] out object? ppObj);

    [PreserveSig]
    int TryGetToken(out ModuleToken pToken);

    [PreserveSig]
    int TryIsDynamic([MarshalAs(UnmanagedType.Bool)] out bool pDynamic);

    [PreserveSig]
    int TryGetGlobalVariableValue(FieldDefToken fieldDef, out ICorDebugValue ppValue);

    [PreserveSig]
    int TryGetSize(out uint pcBytes);

    [PreserveSig]
    int TryIsInMemory([MarshalAs(UnmanagedType.Bool)] out bool pInMemory);
}