using System.Text.Json.Serialization;

namespace DotNet.Debugging.Adapter;

public enum DebugTarget {
    CoreClr,
    Android,
    IOS,
    Maccatalyst,
}

public class LaunchSettings {
    [JsonPropertyName("profiles")]
    public Dictionary<string, LaunchProfile>? Profiles { get; set; }
}

public class LaunchProfile {
    [JsonPropertyName("applicationUrl")]
    public string? ApplicationUrl { get; set; }

    [JsonPropertyName("environmentVariables")]
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    [JsonPropertyName("commandLineArgs")]
    public string? CommandLineArgs { get; set; }

    [JsonPropertyName("executablePath")]
    public string? ExecutablePath { get; set; }

    [JsonPropertyName("workingDirectory")]
    public string? workingDirectory { get; set; }

    [JsonPropertyName("launchUrl")]
    public string? LaunchUrl { get; set; }

    [JsonPropertyName("launchBrowser")]
    public bool LaunchBrowser { get; set; }
}

public class CoreClrMobileDebuggerOptions {
    [JsonPropertyName("runtimeIdentifier")]
    public string? RuntimeIdentifier { get; set; }

    [JsonPropertyName("platform")]
    public DebugTarget Platform { get; set; }

    [JsonPropertyName("ip")]
    public string? Address { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("tcpTunnel")]
    public int[]? TcpTunnel { get; set; }

    [JsonPropertyName("assetsPath")]
    public string? AssetsPath { get; set; }

    [JsonPropertyName("device")]
    public string? Device { get; set; }

    [JsonPropertyName("isDevice")]
    public bool IsDevice { get; set; }

    [JsonPropertyName("uninstallApp")]
    public bool UninstallApp { get; set; }


    [JsonIgnore]
    public bool IsServer => Platform == DebugTarget.Maccatalyst || (Platform == DebugTarget.IOS && !IsDevice);
}

public class SourceLinkOptions {
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public class SymbolOptions {
    [JsonPropertyName("searchPaths")]
    public List<string> SearchPaths { get; set; } = new List<string>();

    [JsonPropertyName("searchMicrosoftSymbolServer")]
    public bool SearchMicrosoftSymbolServer { get; set; }

    [JsonPropertyName("searchNuGetOrgSymbolServer")]
    public bool SearchNuGetSymbolServer { get; set; }

    [JsonPropertyName("cachePath")]
    public string? CachePath { get; set; }

    // "moduleFilter": {
    //     "mode": "loadAllButExcluded",
    //     "excludedModules": [ "DoNotLookForThisOne*.dll" ]
    // }
}

public class LoggingOptions {
    [JsonPropertyName("exceptions")]
    public bool Exceptions { get; set; }

    [JsonPropertyName("engineLogging")]
    public bool EngineLogging { get; set; }

    [JsonPropertyName("elapsedTiming")]
    public bool ElapsedTiming { get; set; }

    // [JsonPropertyName("browserStdOut")]
    // public bool BrowserStdOut { get; set; }
}