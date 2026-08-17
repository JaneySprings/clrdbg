using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[CustomMarshaller(typeof(CorDebugGuidToTypeMapping), MarshalMode.Default, typeof(CorDebugGuidToTypeMappingMarshaller))]
internal static class CorDebugGuidToTypeMappingMarshaller {
    internal struct Native {
        public Guid iid;
        public nint pType;
    }

    public unsafe static Native ConvertToUnmanaged(in CorDebugGuidToTypeMapping managed) {
        return new Native {
            iid = managed.iid,
            pType = (nint)ComInterfaceMarshaller<ICorDebugType>.ConvertToUnmanaged(managed.pType)
        };
    }

    public unsafe static CorDebugGuidToTypeMapping ConvertToManaged(in Native unmanaged) {
        return new CorDebugGuidToTypeMapping {
            iid = unmanaged.iid,
            pType = (ComInterfaceMarshaller<ICorDebugType>.ConvertToManaged((void*)unmanaged.pType) ?? throw new InvalidOperationException("Native CorDebugGuidToTypeMapping.pType was null."))
        };
    }

    public unsafe static void Free(in Native unmanaged) {
        ComInterfaceMarshaller<ICorDebugType>.Free((void*)unmanaged.pType);
    }
}