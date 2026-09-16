using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Engine.Evaluation;

// The runtime refused to start an evaluation on the thread: it is not stopped at a point where code can run on it.
// Nothing runs on that thread until the debuggee moves on, so a caller can spare itself further attempts
public class EvaluationRefusedException : EvaluationException {
    public EvaluationRefusedException(string reason) : base($"Cannot evaluate the expression: {reason}") { }

    public static bool IsRefusal(int result) {
        return result == Cor.CORDBG_E_ILLEGAL_AT_GC_UNSAFE_POINT
            || result == Cor.CORDBG_E_ILLEGAL_IN_NATIVE_CODE
            || result == Cor.CORDBG_E_ILLEGAL_IN_PROLOG
            || result == Cor.CORDBG_E_ILLEGAL_IN_OPTIMIZED_CODE
            || result == Cor.CORDBG_E_ILLEGAL_IN_STACK_OVERFLOW
            || result == Cor.CORDBG_E_FUNC_EVAL_BAD_START_POINT;
    }
}
