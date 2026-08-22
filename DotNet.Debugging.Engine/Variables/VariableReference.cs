using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Engine.Variables;

internal enum VariableReferenceKind {
    // The locals, arguments and current exception of a frame
    Scope,
    // The children (members or elements) of a value
    Members,
    // The 'Static members' group of a value
    StaticMembers,
    // The 'Non-Public members' group of a value
    NonPublicMembers,
    // The 'Non-Public members' group of a 'Static members' group
    NonPublicStaticMembers,
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

    public VariableReference(VariableReferenceKind kind, int threadId, int frameDepth, ICorDebugValue? value = null, ICorDebugValue? proxyValue = null, string? evaluateName = null) {
        Kind = kind;
        ThreadId = threadId;
        FrameDepth = frameDepth;
        Value = value;
        ProxyValue = proxyValue;
        EvaluateName = evaluateName;
    }
}
