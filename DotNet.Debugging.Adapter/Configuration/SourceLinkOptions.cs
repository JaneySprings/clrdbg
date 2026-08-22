using System.Text.Json.Serialization;

namespace DotNet.Debugging.Adapter;

// The value of a 'sourceLinkOptions' entry, keyed by a URL pattern with '*' wildcards ("*", "https://raw.githubusercontent.com/*")
public class SourceLinkOptions {
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
