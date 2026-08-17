using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugexceptionobjectstackframe-structure
[NativeMarshalling(typeof(CorDebugExceptionObjectStackFrameMarshaller))]
public struct CorDebugExceptionObjectStackFrame {
    public ICorDebugModule pModule;
    public CordbAddress ip;
    public MethodDefToken methodDef;
    public int isLastForeignExceptionFrame;
}