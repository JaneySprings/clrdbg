using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugguidtotypemapping-structure
[NativeMarshalling(typeof(CorDebugGuidToTypeMappingMarshaller))]
public struct CorDebugGuidToTypeMapping {
    public Guid iid;
    public ICorDebugType pType;
}