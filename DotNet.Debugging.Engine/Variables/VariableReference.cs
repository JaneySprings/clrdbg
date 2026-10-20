using DotNet.Debugging.CorApi;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Engine.Variables;

internal enum VariableReferenceKind {
    // The locals, arguments and current exception of a frame
    Scope,
    // The current exception alone: what a frame of the exception's recorded stack trace has to show, its locals went with the frame
    ExceptionScope,
    // The children (members or elements) of a value
    Members,
    // The 'Static members' group of a value
    StaticMembers,
    // The 'Non-Public members' group of a value
    NonPublicMembers,
    // The value's own members listed under the 'Raw View' group of a DebuggerTypeProxy expansion
    RawMembers,
    // The 'Results View' node of a value implementing IEnumerable, enumerated when expanded
    ResultsView,
    // A property listed without its value (implicit evaluation is off): the getter runs when the client asks for it
    LazyValue,
}

// What a variables reference handed to the client stands for
internal class VariableReference {
    public VariableReferenceKind Kind { get; }
    public int ThreadId { get; }
    public int FrameDepth { get; }
    public ICorDebugValue? Value { get; }
    // The DebuggerTypeProxy instance whose public members are listed instead of the value's own
    public ICorDebugValue? ProxyValue { get; }
    // The expression of the value whose children this reference lists, so the children can build their own ('parent.Member', 'parent[0]')
    public string? EvaluateName { get; }
    // The names a tuple typed local gives its elements, shown instead of 'Item1', 'Item2'...; null for any other value
    public string[]? TupleElementNames { get; set; }
    // The name of a lazy value, and the read that produces it (the listed variable, as its getter would have made it)
    public string? LazyName { get; }
    public Func<Task<VariableInfo?>>? ReadLazyValueAsync { get; }

    public VariableReference(VariableReferenceKind kind, int threadId, int frameDepth, ICorDebugValue? value = null, ICorDebugValue? proxyValue = null, string? evaluateName = null) {
        Kind = kind;
        ThreadId = threadId;
        FrameDepth = frameDepth;
        Value = value;
        ProxyValue = proxyValue;
        EvaluateName = evaluateName;
    }
    public VariableReference(int threadId, int frameDepth, string lazyName, Func<Task<VariableInfo?>> readLazyValueAsync) {
        Kind = VariableReferenceKind.LazyValue;
        ThreadId = threadId;
        FrameDepth = frameDepth;
        LazyName = lazyName;
        ReadLazyValueAsync = readLazyValueAsync;
    }
}
