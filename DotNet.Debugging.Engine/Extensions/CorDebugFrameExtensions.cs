using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;

namespace DotNet.Debugging.Engine.Extensions;

internal static class CorDebugFrameExtensions {
    // The native address the frame is executing at: the start of the jitted code plus the native offset
    public static ulong? GetInstructionPointer(this ICorDebugFrame frame, ICorDebugFunction function) {
        try {
            if (frame is not ICorDebugNativeFrame nativeFrame)
                return null;
            return function.GetNativeCode().GetAddress().Value + (ulong)nativeFrame.GetIP();
        }
        catch {
            // Not jitted yet, or no native view of the frame
            return null;
        }
    }
    public static string GetDisplayName(this ICorDebugInternalFrame frame) {
        return frame.GetFrameType() switch {
            CorDebugInternalFrameType.STUBFRAME_M2U => "[Managed to Native Transition]",
            CorDebugInternalFrameType.STUBFRAME_U2M => "[Native to Managed Transition]",
            CorDebugInternalFrameType.STUBFRAME_APPDOMAIN_TRANSITION => "[Appdomain Transition]",
            CorDebugInternalFrameType.STUBFRAME_LIGHTWEIGHT_FUNCTION => "[Lightweight Function]",
            CorDebugInternalFrameType.STUBFRAME_FUNC_EVAL => "[Function Evaluation]",
            CorDebugInternalFrameType.STUBFRAME_INTERNALCALL => "[Internal Call]",
            CorDebugInternalFrameType.STUBFRAME_CLASS_INIT => "[Class Initialization]",
            CorDebugInternalFrameType.STUBFRAME_EXCEPTION => "[Exception]",
            CorDebugInternalFrameType.STUBFRAME_SECURITY => "[Security]",
            CorDebugInternalFrameType.STUBFRAME_JIT_COMPILATION => "[JIT Compilation]",
            _ => "[Unknown]"
        };
    }
    // 'Method IL_0010' for the log, the kind of frame when there is no IL frame
    public static string Describe(this ICorDebugFrame? frame) {
        if (frame is not ICorDebugILFrame ilFrame)
            return frame == null ? "no frame" : "non-IL frame";
        var function = ilFrame.GetFunction();
        var methodName = function.GetModule().GetMetaDataInterface<IMetaDataImport>().GetMethodProps(function.GetToken()).szMethod;
        return $"{methodName} IL_{ilFrame.GetIP().pnOffset:X4}";
    }
}
