using System.Collections.Immutable;
using System.Reflection.Metadata;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;
using DotNet.Debugging.Engine.Extensions;

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
    // The keys' static type is unsigned, which their host values do not show
    public bool IsUnsigned { get; }

    public HostOrdering(List<CilValue> keys, bool descending, bool isUnsigned) {
        Keys = keys;
        Descending = descending;
        IsUnsigned = isUnsigned;
    }
}

// A Span<T> or ReadOnlySpan<T> the expression built, over a string, a debuggee array or values the interpreter holds.
// A byref-like struct cannot travel through a func eval, so the span forms the compiler lowers to run on the host
// ('SpanEmulator'); the array is kept by its source reference, which the func evals in between do not neuter
internal class HostSpan {
    private readonly string? text;
    private readonly ICorDebugValue? arraySource;
    private readonly List<CilValue>? items;
    private readonly int start;

    public ResolvedCilType ElementType { get; }
    public int Length { get; }

    private HostSpan(ResolvedCilType elementType, string? text, ICorDebugValue? arraySource, List<CilValue>? items, int start, int length) {
        ElementType = elementType;
        this.text = text;
        this.arraySource = arraySource;
        this.items = items;
        this.start = start;
        Length = length;
    }

    public static HostSpan OverText(ResolvedCilType elementType, string text) {
        return new HostSpan(elementType, text, null, null, 0, text.Length);
    }
    public static HostSpan OverArray(ResolvedCilType elementType, ICorDebugValue arraySource, int length) {
        return new HostSpan(elementType, null, arraySource, null, 0, length);
    }
    // 'items' are values, or the locations of the variables the span was created over
    public static HostSpan OverItems(ResolvedCilType elementType, List<CilValue> items) {
        return new HostSpan(elementType, null, null, items, 0, items.Count);
    }

    // The location of an element: the debuggee's slot for an array, the variable's for a span over one, a temporary otherwise
    public ICilLocation GetItem(int index) {
        if (index < 0 || index >= Length)
            throw new EvaluationThrewException("System.IndexOutOfRangeException");
        if (text != null)
            return new TemporaryLocation(CilValue.FromPrimitive(text[start + index]));
        if (arraySource != null)
            return new CorDebugLocation(arraySource.GetArrayValue().GetElementAtPosition(start + index));
        var item = items![start + index];
        return item.Location ?? new TemporaryLocation(item);
    }
    public CilValue Read(int index) {
        return GetItem(index).Read();
    }
    public HostSpan Slice(int offset, int length) {
        if (offset < 0 || length < 0 || offset + length > Length)
            throw new EvaluationThrewException("System.ArgumentOutOfRangeException");
        return new HostSpan(ElementType, text, arraySource, items, start + offset, length);
    }
    // The characters of a span of chars
    public string GetText() {
        if (text != null)
            return text.Substring(start, Length);
        var characters = new char[Length];
        for (var i = 0; i < Length; i++)
            characters[i] = (char)Read(i).AsInt32();
        return new string(characters);
    }
    public List<CilValue> ToList() {
        var result = new List<CilValue>(Length);
        for (var i = 0; i < Length; i++)
            result.Add(Read(i));
        return result;
    }
}
