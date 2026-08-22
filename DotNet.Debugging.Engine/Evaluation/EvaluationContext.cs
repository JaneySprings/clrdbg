using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Engine.Evaluation;

internal class EvaluationContext {
    public ICorDebugThread Thread { get; }
    public int ThreadId { get; }
    public int FrameDepth { get; }
    // When set, identifiers resolve against this value instead of the frame: DebuggerDisplay expressions only see the displayed object
    public ICorDebugValue? RootValue { get; }

    public EvaluationContext(ICorDebugThread thread, int threadId, int frameDepth, ICorDebugValue? rootValue = null) {
        Thread = thread;
        ThreadId = threadId;
        FrameDepth = frameDepth;
        RootValue = rootValue;
    }
}
