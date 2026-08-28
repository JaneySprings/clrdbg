using System.Runtime.InteropServices.Marshalling;

namespace DotNet.Debugging.CorApi;

// https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cordebugexceptionobjectstackframe-structure
[NativeMarshalling(typeof(CorDebugExceptionObjectStackFrameMarshaller))]
public struct CorDebugExceptionObjectStackFrame {
    // Null for a frame whose module the runtime could not resolve (e.g. a dynamic method)
    public ICorDebugModule? pModule;
    public CordbAddress ip;
    public MethodDefToken methodDef;
    public int isLastForeignExceptionFrame;
}