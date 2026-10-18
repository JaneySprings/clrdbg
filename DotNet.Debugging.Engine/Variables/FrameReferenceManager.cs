namespace DotNet.Debugging.Engine.Variables;

internal class FrameReference {
    public int ThreadId { get; }
    // The depth in the thread's stack; negative for a frame of the exception's recorded stack trace, which the stack no longer holds
    public int Depth { get; }
    public bool IsRecorded => Depth < 0;

    public FrameReference(int threadId, int depth) {
        ThreadId = threadId;
        Depth = depth;
    }
}

// Issues the frame ids handed to the client. A frame is identified by its thread and depth, as the
// ICorDebugFrame objects are neutered by any continue and have to be re-obtained
internal class FrameReferenceManager {
    private readonly Dictionary<int, FrameReference> references = new Dictionary<int, FrameReference>();
    private readonly Dictionary<(int ThreadId, int Depth), int> ids = new Dictionary<(int ThreadId, int Depth), int>();
    private int nextId = 1;

    public int GetOrCreate(int threadId, int depth) {
        if (ids.TryGetValue((threadId, depth), out var id))
            return id;

        id = nextId++;
        references[id] = new FrameReference(threadId, depth);
        ids[(threadId, depth)] = id;
        return id;
    }
    public int GetOrCreateRecorded(int threadId, int index) {
        return GetOrCreate(threadId, -1 - index);
    }
    public FrameReference? Get(int id) {
        return references.GetValueOrDefault(id);
    }
    public void Clear() {
        references.Clear();
        ids.Clear();
        nextId = 1;
    }
}
