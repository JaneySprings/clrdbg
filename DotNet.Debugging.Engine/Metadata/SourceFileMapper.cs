namespace DotNet.Debugging.Engine.Metadata;

internal class SourceFileMapper {
    public IReadOnlyDictionary<string, string> Map { get; }

    public SourceFileMapper(IReadOnlyDictionary<string, string> map) {
        Map = map;
    }

    // The longest matching prefix wins, so a more specific entry beats a broader one
    public string ToLocalPath(string filePath) {
        var result = filePath;
        var matchLength = -1;
        foreach (var entry in Map) {
            if (entry.Key.Length > matchLength && TryReplacePrefix(filePath, entry.Key, entry.Value, out var replaced)) {
                result = replaced;
                matchLength = entry.Key.Length;
            }
        }
        return result;
    }

    // Replaces the 'prefix' part of the path by 'replacement'. The prefix must end on a directory
    // boundary, and separators do not have to agree: the rest of the path takes the separator flavor
    // of the replacement, so 'C:\src' -> '/home/src' turns 'C:\src\A.cs' into '/home/src/A.cs'
    private static bool TryReplacePrefix(string filePath, string prefix, string replacement, out string result) {
        result = filePath;
        var normalizedPath = filePath.Replace('\\', '/');
        var normalizedPrefix = TrimEndSeparator(prefix.Replace('\\', '/'));
        if (!normalizedPath.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var remainder = filePath.Substring(normalizedPrefix.Length);
        if (remainder.Length > 0 && remainder[0] != '/' && remainder[0] != '\\')
            return false;

        var separator = replacement.Contains('\\') ? '\\' : '/';
        result = TrimEndSeparator(replacement) + remainder.Replace('/', separator).Replace('\\', separator);
        return true;
    }
    private static string TrimEndSeparator(string path) {
        return path.TrimEnd('/', '\\');
    }
}
