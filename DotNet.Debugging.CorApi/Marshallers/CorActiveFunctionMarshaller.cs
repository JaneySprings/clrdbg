using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[CustomMarshaller(typeof(CorActiveFunction), MarshalMode.Default, typeof(CorActiveFunctionMarshaller))]
internal static class CorActiveFunctionMarshaller {
    internal struct Native {
        public nint pAppDomain;

        public nint pModule;

        public nint pFunction;

        public uint ilOffset;

        public uint flags;
    }

    public unsafe static Native ConvertToUnmanaged(in CorActiveFunction managed) {
        return new Native {
            pAppDomain = (nint)ComInterfaceMarshaller<ICorDebugAppDomain>.ConvertToUnmanaged(managed.pAppDomain),
            pModule = (nint)ComInterfaceMarshaller<ICorDebugModule>.ConvertToUnmanaged(managed.pModule),
            pFunction = (nint)ComInterfaceMarshaller<ICorDebugFunction2>.ConvertToUnmanaged(managed.pFunction),
            ilOffset = managed.ilOffset,
            flags = managed.flags
        };
    }

    public unsafe static CorActiveFunction ConvertToManaged(in Native unmanaged) {
        return new CorActiveFunction {
            pAppDomain = (ComInterfaceMarshaller<ICorDebugAppDomain>.ConvertToManaged((void*)unmanaged.pAppDomain) ?? throw new InvalidOperationException("Native CorActiveFunction.pAppDomain was null.")),
            pModule = (ComInterfaceMarshaller<ICorDebugModule>.ConvertToManaged((void*)unmanaged.pModule) ?? throw new InvalidOperationException("Native CorActiveFunction.pModule was null.")),
            pFunction = (ComInterfaceMarshaller<ICorDebugFunction2>.ConvertToManaged((void*)unmanaged.pFunction) ?? throw new InvalidOperationException("Native CorActiveFunction.pFunction was null.")),
            ilOffset = unmanaged.ilOffset,
            flags = unmanaged.flags
        };
    }

    public unsafe static void Free(in Native unmanaged) {
        ComInterfaceMarshaller<ICorDebugAppDomain>.Free((void*)unmanaged.pAppDomain);
        ComInterfaceMarshaller<ICorDebugModule>.Free((void*)unmanaged.pModule);
        ComInterfaceMarshaller<ICorDebugFunction2>.Free((void*)unmanaged.pFunction);
    }
}