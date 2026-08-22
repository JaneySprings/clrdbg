namespace DotNet.Debugging.Engine.Variables;

internal class FrameReference {
    public int ThreadId { get; }
    public int Depth { get; }

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
    public FrameReference? Get(int id) {
        return references.GetValueOrDefault(id);
    }
    public void Clear() {
        references.Clear();
        ids.Clear();
        nextId = 1;
    }
}
