using System.Diagnostics.CodeAnalysis;
using DotNet.Debugging.CorApi;
using DotNet.Debugging.CorApi.Extensions;

namespace DotNet.Debugging.Engine.Evaluation;

// Tracks the strong handles created during an evaluation so they are released once it is done
internal class EvaluationHandleScope : IDisposable {
    private readonly HashSet<ICorDebugHandleValue> handles = new HashSet<ICorDebugHandleValue>(ReferenceEqualityComparer.Instance);

    [return: NotNullIfNotNull(nameof(value))]
    public T? Track<T>(T? value) where T : ICorDebugValue {
        if (value is ICorDebugHandleValue handle)
            handles.Add(handle);
        return value;
    }
    // Pins a reference value with a strong handle, so it survives the func evals the interpreter runs later
    public CilValue Root(CilValue value) {
        if (value.CorValue is ICorDebugHandleValue)
            return value;
        if (value.CorValue is not ICorDebugReferenceValue reference || reference.IsNull() || reference.GetElementType() == CorElementType.BYREF)
            return value;
        return CilValue.FromCorValue(CreateHandle(reference));
    }
    public ICorDebugHandleValue CreateHandle(ICorDebugReferenceValue reference) {
        if (reference.Dereference() is not ICorDebugHeapValue2 heapValue)
            throw new InvalidOperationException("The referenced debuggee value cannot be rooted");
        var handle = heapValue.CreateHandle(CorDebugHandleType.HANDLE_STRONG);
        handles.Add(handle);
        return handle;
    }
    // Removes the handle from the scope when it is one of ours, the caller is then responsible for releasing it
    public ICorDebugHandleValue? Detach(ICorDebugValue? value) {
        if (value is not ICorDebugHandleValue handle)
            return null;
        return handles.Remove(handle) ? handle : null;
    }

    public void Dispose() {
        foreach (var handle in handles)
            handle.TryDispose();
        handles.Clear();
    }
}
