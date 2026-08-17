using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugblockingobject-structure
[NativeMarshalling(typeof(CorDebugBlockingObjectMarshaller))]
public struct CorDebugBlockingObject {
    public ICorDebugValue pBlockingObject;
    public uint dwTimeout;
    public CorDebugBlockingReason blockingReason;
}