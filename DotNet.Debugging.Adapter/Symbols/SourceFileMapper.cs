namespace DotNet.Debugging.Adapter.Symbols;

public class SourceFileMapper {
    private readonly Dictionary<string, string> map;

    public SourceFileMapper(Dictionary<string, string> map) {
        this.map = map;
    }

    // The path of a PDB document, translated for the client
    public string ToLocalPath(string filePath) {
        return Translate(filePath, useCompilerPrefix: true);
    }
    // A path sent by the client, translated to the compile-time form the PDB documents use
    public string ToCompilerPath(string filePath) {
        return Translate(filePath, useCompilerPrefix: false);
    }

    // The longest matching prefix wins, so a more specific entry beats a broader one
    private string Translate(string filePath, bool useCompilerPrefix) {
        var result = filePath;
        var matchLength = -1;
        foreach (var entry in map) {
            var prefix = useCompilerPrefix ? entry.Key : entry.Value;
            var replacement = useCompilerPrefix ? entry.Value : entry.Key;
            if (prefix.Length > matchLength && TryReplacePrefix(filePath, prefix, replacement, out var replaced)) {
                result = replaced;
                matchLength = prefix.Length;
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
