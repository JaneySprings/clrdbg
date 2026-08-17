namespace DotNet.Debugging.Adapter;

public class Handles<T> where T : class {
    private const int StartHandle = 1000;

    private readonly int startHandle;
    private int nextHandle;
    private readonly Dictionary<int, T> handleMap;

    public Handles() : this(StartHandle) { }
    public Handles(int startHandle) {
        this.startHandle = startHandle;
        nextHandle = startHandle;
        handleMap = new Dictionary<int, T>();
    }

    public void Reset() {
        nextHandle = startHandle;
        handleMap.Clear();
    }

    public int Create(T value) {
        var handle = nextHandle++;
        handleMap[handle] = value;
        return handle;
    }

    public bool TryGet(int handle, out T? value) {
        return handleMap.TryGetValue(handle, out value);
    }

    public T? Get(int handle, T? defaultValue = null) {
        if (handleMap.TryGetValue(handle, out T? value))
            return value;
        return defaultValue;
    }
}