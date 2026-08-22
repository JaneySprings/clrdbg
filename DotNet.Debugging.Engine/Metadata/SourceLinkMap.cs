using System.Text.Json;

namespace DotNet.Debugging.Engine.Metadata;

// The 'documents' map of a SourceLink blob: document path patterns ('C:\src\*', '/_/*') to download URLs ('https://raw.githubusercontent.com/org/repo/sha/*')
internal class SourceLinkMap {
    private readonly List<SourceLinkEntry> entries;

    private SourceLinkMap(List<SourceLinkEntry> entries) {
        this.entries = entries;
    }

    public static SourceLinkMap? TryParse(string json) {
        try {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("documents", out var documents) || documents.ValueKind != JsonValueKind.Object)
                return null;

            var entries = new List<SourceLinkEntry>();
            foreach (var property in documents.EnumerateObject()) {
                if (property.Value.ValueKind != JsonValueKind.String)
                    continue;
                entries.Add(new SourceLinkEntry(property.Name, property.Value.GetString()!));
            }
            return entries.Count == 0 ? null : new SourceLinkMap(entries);
        }
        catch (JsonException) {
            return null;
        }
    }

    public string? GetUrl(string documentPath) {
        var normalizedPath = NormalizePath(documentPath);
        SourceLinkEntry? bestEntry = null;
        foreach (var entry in entries) {
            if (!entry.Matches(normalizedPath))
                continue;
            // The most specific (longest) pattern wins when several match
            if (bestEntry == null || entry.PathPrefix.Length > bestEntry.PathPrefix.Length)
                bestEntry = entry;
        }
        return bestEntry?.GetUrl(normalizedPath);
    }

    private static string NormalizePath(string path) {
        return path.Replace('\\', '/');
    }

    private class SourceLinkEntry {
        private readonly string urlPrefix;
        private readonly string? urlSuffix;

        public string PathPrefix { get; }
        public bool IsWildcard { get; }

        public SourceLinkEntry(string pathPattern, string urlPattern) {
            var pathWildcard = pathPattern.IndexOf('*');
            IsWildcard = pathWildcard >= 0;
            PathPrefix = NormalizePath(IsWildcard ? pathPattern.Substring(0, pathWildcard) : pathPattern);

            var urlWildcard = urlPattern.IndexOf('*');
            urlPrefix = urlWildcard >= 0 ? urlPattern.Substring(0, urlWildcard) : urlPattern;
            urlSuffix = urlWildcard >= 0 ? urlPattern.Substring(urlWildcard + 1) : null;
        }

        public bool Matches(string normalizedPath) {
            if (IsWildcard)
                return normalizedPath.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase);
            return string.Equals(normalizedPath, PathPrefix, StringComparison.OrdinalIgnoreCase);
        }
        public string GetUrl(string normalizedPath) {
            if (!IsWildcard || urlSuffix == null)
                return urlPrefix;
            var relativePath = normalizedPath.Substring(PathPrefix.Length);
            return urlPrefix + relativePath + urlSuffix;
        }
    }
}
