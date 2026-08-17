using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[CustomMarshaller(typeof(CorDebugBlockingObject), MarshalMode.Default, typeof(CorDebugBlockingObjectMarshaller))]
internal static class CorDebugBlockingObjectMarshaller {
    internal struct Native {
        public nint pBlockingObject;

        public uint dwTimeout;

        public CorDebugBlockingReason blockingReason;
    }

    public unsafe static Native ConvertToUnmanaged(in CorDebugBlockingObject managed) {
        return new Native {
            pBlockingObject = (nint)ComInterfaceMarshaller<ICorDebugValue>.ConvertToUnmanaged(managed.pBlockingObject),
            dwTimeout = managed.dwTimeout,
            blockingReason = managed.blockingReason
        };
    }

    public unsafe static CorDebugBlockingObject ConvertToManaged(in Native unmanaged) {
        return new CorDebugBlockingObject {
            pBlockingObject = (ComInterfaceMarshaller<ICorDebugValue>.ConvertToManaged((void*)unmanaged.pBlockingObject) ?? throw new InvalidOperationException("Native CorDebugBlockingObject.pBlockingObject was null.")),
            dwTimeout = unmanaged.dwTimeout,
            blockingReason = unmanaged.blockingReason
        };
    }

    public unsafe static void Free(in Native unmanaged) {
        ComInterfaceMarshaller<ICorDebugValue>.Free((void*)unmanaged.pBlockingObject);
    }
}