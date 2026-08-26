using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Engine.Variables;

// Issues the variables references handed to the client and owns the debuggee handles kept alive behind them
internal class VariableManager {
    private readonly Dictionary<int, VariableReference> references = new Dictionary<int, VariableReference>();
    private readonly List<ICorDebugHandleValue> ownedHandles = new List<ICorDebugHandleValue>();
    private int nextReference = 1;

    public int Create(VariableReference reference) {
        var id = nextReference++;
        references[id] = reference;
        return id;
    }
    public VariableReference? Get(int id) {
        return references.GetValueOrDefault(id);
    }
    // Takes ownership of a handle stored behind no reference (a 'Results View' array whose elements are handed out)
    public void Keep(ICorDebugHandleValue handle) {
        ownedHandles.Add(handle);
    }

    // Handles are released once the debuggee continues, as the values they point to are gone by the next stop
    public void Clear() {
        // The same handle can be stored behind several references (a value and its 'Static members' group)
        var handles = new HashSet<ICorDebugHandleValue>(ReferenceEqualityComparer.Instance);
        foreach (var reference in references.Values) {
            if (reference.Value is ICorDebugHandleValue valueHandle)
                handles.Add(valueHandle);
            if (reference.ProxyValue is ICorDebugHandleValue proxyHandle)
                handles.Add(proxyHandle);
        }
        foreach (var handle in ownedHandles)
            handles.Add(handle);
        foreach (var handle in handles)
            handle.TryDispose();
        references.Clear();
        ownedHandles.Clear();
        nextReference = 1;
    }
}
