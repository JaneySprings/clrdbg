using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace DotNet.Debugging.Engine.Evaluation;

// An instance of a type the expression assembly declares (a lambda's closure class, a display class holding the
// captured variables, an anonymous type). Such types do not exist in the debuggee, so the object lives on the host
// and its fields hold interpreter values
internal class HostObject {
    private readonly Dictionary<FieldDefinitionHandle, ICilLocation> fields = new Dictionary<FieldDefinitionHandle, ICilLocation>();

    public TypeDefinitionHandle Type { get; }
    // The instantiation the object was created through, the generic context of its methods
    public ImmutableArray<ResolvedCilType> TypeArguments { get; }

    public HostObject(TypeDefinitionHandle type, ImmutableArray<ResolvedCilType> typeArguments) {
        Type = type;
        TypeArguments = typeArguments;
    }

    public ICilLocation GetField(FieldDefinitionHandle field) {
        if (!fields.TryGetValue(field, out var location)) {
            location = new TemporaryLocation(CilValue.Null());
            fields[field] = location;
        }
        return location;
    }
}

// What 'ldftn' pushes: the method a delegate is being built over, one of the expression assembly (a lambda body,
// a local function) or a debuggee method (a method group)
internal class HostFunction {
    public ResolvedEvaluationMethod? EvaluationMethod { get; }
    public ResolvedRuntimeMethod? RuntimeMethod { get; }

    public HostFunction(ResolvedEvaluationMethod evaluationMethod) {
        EvaluationMethod = evaluationMethod;
    }
    public HostFunction(ResolvedRuntimeMethod runtimeMethod) {
        RuntimeMethod = runtimeMethod;
    }
}

// A delegate the expression created. It cannot exist in the debuggee (a lambda has no code there, a method group
// no function pointer here), so the interpreter invokes it itself: on an Invoke call, or per element when it is
// handed to a System.Linq operator
internal class HostDelegate {
    // Null for a static method
    public CilValue? Target { get; }
    public HostFunction Function { get; }

    public HostDelegate(CilValue? target, HostFunction function) {
        Target = target;
        Function = function;
    }
}

// The result of a System.Linq operator the interpreter ran: materialized into a debuggee array of 'ElementType'
// when it is handed to the debuggee or shown as the result
internal class HostSequence {
    public ResolvedCilType ElementType { get; }
    public List<CilValue> Items { get; }
    // The keys the items are sorted by, kept for a ThenBy that follows
    public List<HostOrdering> Orderings { get; } = new List<HostOrdering>();

    public HostSequence(ResolvedCilType elementType, List<CilValue> items) {
        ElementType = elementType;
        Items = items;
    }
}

internal class HostOrdering {
    // One key per item, in the items' order
    public List<CilValue> Keys { get; }
    public bool Descending { get; }

    public HostOrdering(List<CilValue> keys, bool descending) {
        Keys = keys;
        Descending = descending;
    }
}
