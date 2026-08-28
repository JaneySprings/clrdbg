using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

[CustomMarshaller(typeof(CorDebugExceptionObjectStackFrame), MarshalMode.Default, typeof(CorDebugExceptionObjectStackFrameMarshaller))]
internal static class CorDebugExceptionObjectStackFrameMarshaller {
    internal struct Native {
        public nint pModule;
        public CordbAddress ip;
        public MethodDefToken methodDef;
        public int isLastForeignExceptionFrame;
    }

    public unsafe static Native ConvertToUnmanaged(in CorDebugExceptionObjectStackFrame managed) {
        return new Native {
            pModule = (nint)ComInterfaceMarshaller<ICorDebugModule>.ConvertToUnmanaged(managed.pModule),
            ip = managed.ip,
            methodDef = managed.methodDef,
            isLastForeignExceptionFrame = managed.isLastForeignExceptionFrame
        };
    }

    public unsafe static CorDebugExceptionObjectStackFrame ConvertToManaged(in Native unmanaged) {
        return new CorDebugExceptionObjectStackFrame {
            pModule = ComInterfaceMarshaller<ICorDebugModule>.ConvertToManaged((void*)unmanaged.pModule),
            ip = unmanaged.ip,
            methodDef = unmanaged.methodDef,
            isLastForeignExceptionFrame = unmanaged.isLastForeignExceptionFrame
        };
    }

    public unsafe static void Free(in Native unmanaged) {
        ComInterfaceMarshaller<ICorDebugModule>.Free((void*)unmanaged.pModule);
    }
}