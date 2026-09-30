namespace DotNet.Debugging.Remote.Proxy;

internal sealed class BlobComparer : IEqualityComparer<byte[]> {
    public static BlobComparer Instance { get; } = new BlobComparer();

    public bool Equals(byte[]? first, byte[]? second) {
        if (first == null || second == null)
            return first == second;
        return first.AsSpan().SequenceEqual(second);
    }
    public int GetHashCode(byte[] bytes) {
        var hash = new HashCode();
        hash.AddBytes(bytes);
        return hash.ToHashCode();
    }
}
