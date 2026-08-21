namespace DotNet.Debugging.Adapter;

public class Handles<T> where T : class {
    private const int StartHandle = 1000;

    private readonly int startHandle;
    private readonly Dictionary<int, T> handleMap;
    private readonly IEqualityComparer<T?>? comparer;
    private int nextHandle;

    public Handles(IEqualityComparer<T?>? comparer = null) : this(StartHandle, comparer) { }
    public Handles(int startHandle, IEqualityComparer<T?>? comparer = null) {
        this.startHandle = startHandle;
        this.comparer = comparer;
        nextHandle = startHandle;
        handleMap = new Dictionary<int, T>();
    }

    public void Reset() {
        nextHandle = startHandle;
        handleMap.Clear();
    }
    public int Create(T value) {
        if (comparer != null) {
            var existed = FindHandle(value);
            if (existed.HasValue)
                return existed.Value;
        }
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
    public int? FindHandle(T? value) {
        ArgumentNullException.ThrowIfNull(comparer);
        foreach (var kvp in handleMap) {
            if (comparer.Equals(kvp.Value, value))
                return kvp.Key;
        }
        return null;
    }
}