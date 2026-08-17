using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/icordebugprocess5-interface
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("21E9D9C0-FCB8-11DF-8CFF-0800200C9A66")]
public partial interface ICorDebugProcess5 {
    [PreserveSig]
    int TryGetGCHeapInformation(out CorHeapInfo pHeapInfo);

    [PreserveSig]
    int TryEnumerateHeap(out ICorDebugHeapEnum ppObjects);

    [PreserveSig]
    int TryEnumerateHeapRegions(out ICorDebugHeapSegmentEnum ppRegions);

    [PreserveSig]
    int TryGetObject(CordbAddress addr, out ICorDebugObjectValue pObject);

    [PreserveSig]
    int TryEnumerateGCReferences([MarshalAs(UnmanagedType.Bool)] bool enumerateWeakReferences, out ICorDebugGCReferenceEnum ppEnum);

    [PreserveSig]
    int TryEnumerateHandles(CorGCReferenceType types, out ICorDebugGCReferenceEnum ppEnum);

    [PreserveSig]
    int TryGetTypeID(CordbAddress obj, out CorTypeId pId);

    [PreserveSig]
    int TryGetTypeForTypeID(CorTypeId id, out ICorDebugType ppType);

    [PreserveSig]
    int TryGetArrayLayout(CorTypeId id, out CorArrayLayout pLayout);

    [PreserveSig]
    int TryGetTypeLayout(CorTypeId id, out CorTypeLayout pLayout);

    [PreserveSig]
    int TryGetTypeFields(CorTypeId id, uint celt, [Out][MarshalUsing(CountElementName = "celt")] CorField[]? fields, out uint pceltNeeded);

    [PreserveSig]
    int TryEnableNGENPolicy(CorDebugNGENPolicy ePolicy);
}