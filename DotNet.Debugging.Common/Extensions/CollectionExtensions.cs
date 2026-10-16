namespace DotNet.Debugging.Common.Extensions;

public static class CollectionExtensions {
    public static void ForEach<TValue>(this IEnumerable<TValue> collection, Action<TValue> handler) {
        foreach (var item in collection)
            handler.Invoke(item);
    }
}