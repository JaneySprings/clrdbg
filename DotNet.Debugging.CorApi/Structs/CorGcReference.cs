using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-gc-reference-structure
[NativeMarshalling(typeof(CorGcReferenceMarshaller))]
public struct CorGcReference {
    public ICorDebugAppDomain? Domain;
    public ICorDebugValue Location;
    public CorGCReferenceType Type;
    public ulong ExtraData;
}