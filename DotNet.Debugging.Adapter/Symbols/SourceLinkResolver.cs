using DotNet.Debugging.Common.Logging;

namespace DotNet.Debugging.Adapter.Symbols;

// Serves the sources of modules whose PDB carries a Source Link map when they are not available locally.
// Such a document is reported with a 'sourceReference' and downloaded only when the client opens it ('source' request)
public class SourceLinkResolver {
    private static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly Dictionary<string, SourceLinkOptions> options;
    // The URL behind each reference, stable for the session so the client keeps one document per file
    private readonly Handles<string> sourceHandles;
    private readonly Dictionary<string, string?> contents;
    private readonly CurrentClassLogger logger;
    private readonly object handlesLock;

    public SourceLinkResolver(Dictionary<string, SourceLinkOptions> options) {
        this.options = options;
        logger = new CurrentClassLogger(nameof(SourceLinkResolver));
        sourceHandles = new Handles<string>(StringComparer.Ordinal);
        contents = new Dictionary<string, string?>(StringComparer.Ordinal);
        handlesLock = new object();
    }

    // Zero when the URL is disabled by the 'sourceLinkOptions'
    public int GetSourceReference(string url) {
        if (!IsEnabled(url))
            return 0;
        lock (handlesLock)
            return sourceHandles.Create(url);
    }
    public string? GetSourceContent(int sourceReference) {
        string? url;
        lock (handlesLock)
            url = sourceHandles.Get(sourceReference);
        if (url == null)
            return null;

        if (contents.TryGetValue(url, out var content))
            return content;
        content = Download(url);
        contents[url] = content;
        return content;
    }
    public bool IsEnabled(string url) {
        // The most specific (longest) pattern decides
        var enabled = true;
        var bestLength = -1;
        foreach (var (pattern, option) in options) {
            if (!MatchesPattern(url, pattern) || pattern.Length <= bestLength)
                continue;
            bestLength = pattern.Length;
            enabled = option.Enabled;
        }
        return enabled;
    }

    private string? Download(string url) {
        try {
            logger?.Debug($"Downloading source file from '{url}'");
            return httpClient.GetStringAsync(url).GetAwaiter().GetResult();
        }
        catch (Exception ex) {
            logger?.Error($"Failed to download source file from '{url}': {ex.Message}");
            return null;
        }
    }
    private static bool MatchesPattern(string url, string pattern) {
        var wildcard = pattern.IndexOf('*');
        if (wildcard < 0)
            return string.Equals(url, pattern, StringComparison.OrdinalIgnoreCase);
        var prefix = pattern.Substring(0, wildcard);
        var suffix = pattern.Substring(wildcard + 1);
        return url.Length >= prefix.Length + suffix.Length
            && url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }
}
