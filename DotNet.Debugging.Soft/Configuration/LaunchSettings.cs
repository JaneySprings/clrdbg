using System.Text.Json.Serialization;

namespace DotNet.Debugging.Soft;

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
    [JsonPropertyName("platform")]
    public string? Platform { get; }

    [JsonPropertyName("runtimeIdentifier")]
    public string? RuntimeIdentifier { get; set; }

    [JsonPropertyName("device")] // UDID (iOS) / ADB serial (Android) / null
    public string? Device { get; set; }

    [JsonPropertyName("isSimulator")]
    public bool IsSimulator { get; set; }

    [JsonPropertyName("vsdbgRemoteResources")] // Optional override for the directory that holds the Microsoft remote-debugging native binaries
    public string? VsdbgRemoteResources { get; set; }
}