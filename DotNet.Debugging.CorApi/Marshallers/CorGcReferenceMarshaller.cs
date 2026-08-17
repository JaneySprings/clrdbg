using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[CustomMarshaller(typeof(CorGcReference), MarshalMode.Default, typeof(CorGcReferenceMarshaller))]
internal static class CorGcReferenceMarshaller {
    internal struct Native {
        public nint Domain;
        public nint Location;
        public CorGCReferenceType Type;
        public ulong ExtraData;
    }

    public unsafe static Native ConvertToUnmanaged(in CorGcReference managed) {
        return new Native {
            Domain = (nint)ComInterfaceMarshaller<ICorDebugAppDomain>.ConvertToUnmanaged(managed.Domain),
            Location = (nint)ComInterfaceMarshaller<ICorDebugValue>.ConvertToUnmanaged(managed.Location),
            Type = managed.Type,
            ExtraData = managed.ExtraData
        };
    }

    public unsafe static CorGcReference ConvertToManaged(in Native unmanaged) {
        return new CorGcReference {
            Domain = ComInterfaceMarshaller<ICorDebugAppDomain>.ConvertToManaged((void*)unmanaged.Domain),
            Location = (ComInterfaceMarshaller<ICorDebugValue>.ConvertToManaged((void*)unmanaged.Location) ?? throw new InvalidOperationException("Native CorGcReference.Location was null.")),
            Type = unmanaged.Type,
            ExtraData = unmanaged.ExtraData
        };
    }

    public unsafe static void Free(in Native unmanaged) {
        ComInterfaceMarshaller<ICorDebugAppDomain>.Free((void*)unmanaged.Domain);
        ComInterfaceMarshaller<ICorDebugValue>.Free((void*)unmanaged.Location);
    }
}