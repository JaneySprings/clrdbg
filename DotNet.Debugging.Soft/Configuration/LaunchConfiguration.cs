using System.Text.Json;
using DotNet.Debugging.Common.Extensions;
using DotNet.Debugging.CorApi.Models;
using DotNet.Debugging.Soft.Extensions;
using Newtonsoft.Json.Linq;

namespace DotNet.Debugging.Soft;

public class LaunchConfiguration : BaseConfiguration {
    public string Program { get; private set; }
    public string? WorkingDirectory { get; private set; }
    public List<string> Arguments { get; private set; }
    public Dictionary<string, string> EnvironmentVariables { get; private set; }
    public LaunchRequestConsoleType Console { get; }
    public bool SuppressJITOptimizations { get; }
    public bool StopAtEntry { get; }
    public string? LaunchSettingsFilePath { get; }
    public string? LaunchSettingsProfile { get; }
    public CoreClrMobileDebuggerOptions? MobileOptions { get; }
    // TODO: implement
    public object? PipeTransport { get; }

    public LaunchConfiguration(Dictionary<string, JToken> properties) : base(properties) {
        Program = properties.TryGetValue("program").ToClass<string>().ToPlatformPath();
        WorkingDirectory = properties.TryGetValue("cwd").ToClass<string>().ToPlatformPath();
        Arguments = properties.TryGetValue("args").ToClass<List<string>>() ?? new List<string>();
        EnvironmentVariables = properties.TryGetValue("env").ToClass<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        Console = properties.TryGetValue("console").ToValue<LaunchRequestConsoleType>(LaunchRequestConsoleType.InternalConsole);
        SuppressJITOptimizations = properties.TryGetValue("suppressJITOptimizations").ToValue<bool>(false);
        StopAtEntry = properties.TryGetValue("stopAtEntry").ToValue<bool>(false);
        LaunchSettingsFilePath = properties.TryGetValue("launchSettingsFilePath").ToClass<string>().ToPlatformPath();
        LaunchSettingsProfile = properties.TryGetValue("launchSettingsProfile").ToClass<string>();
        MobileOptions = properties.TryGetValue("coreClrMobileDebuggerOptions").ToClass<CoreClrMobileDebuggerOptions>();

        if (string.IsNullOrEmpty(WorkingDirectory))
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(Program));
        if (!string.IsNullOrEmpty(WorkingDirectory) && !Path.IsPathRooted(Program))
            Program = Path.Combine(WorkingDirectory, Program);
        if (!string.IsNullOrEmpty(WorkingDirectory) && !Path.IsPathRooted(LaunchSettingsFilePath))
            LaunchSettingsFilePath = Path.Combine(WorkingDirectory, LaunchSettingsFilePath);

        if (File.Exists(LaunchSettingsFilePath))
            OverrideFromLaunchSettings(LaunchSettingsFilePath, LaunchSettingsProfile);
    }

    public override IDebugAgent CreateDebugAgent(DebugSession debugSession) {
        if (SkipDebug)
            return new SkipDebugAgent(this, debugSession);
        if (MobileOptions != null)
            return new MobileDebugAgent(this, debugSession);
        return new LaunchDebugAgent(this, debugSession);
    }
    public override string GetApplicationName() {
        return Path.GetFileName(Program);
    }
    public override void VerifyMissingProperties() {
        if (string.IsNullOrEmpty(Program) || (!File.Exists(Program) && !Directory.Exists(Program)))
            throw Session.GetProtocolException(string.Format(Resources.MessageInvalidProgram, Program));

        if (MobileOptions != null) {
            if (string.IsNullOrEmpty(MobileOptions.Platform))
                throw Session.GetProtocolException("The launch configuration 'platform' is required for mobile debugging (e.g. 'ios' or 'maccatalyst').");
            if (string.IsNullOrEmpty(MobileOptions.RuntimeIdentifier))
                throw Session.GetProtocolException("The launch configuration 'runtimeIdentifier' is required for mobile debugging (e.g. 'maccatalyst-arm64').");
            if (MobileOptions.IsSimulator && string.IsNullOrEmpty(MobileOptions.Device))
                throw Session.GetProtocolException("The launch configuration 'device' (simulator UDID) is required to debug on the iOS simulator.");
        }
    }

    public LaunchInfo GetLaunchInfo() {
        ArgumentNullException.ThrowIfNull(Program);
        var info = new LaunchInfo {
            Program = Program,
            Arguments = Arguments,
            Cwd = WorkingDirectory ?? Path.GetDirectoryName(Program),
            Env = EnvironmentVariables,
            StopAtEntry = StopAtEntry,
            LaunchRequestConsoleType = Console
        };

        if (Path.GetExtension(Program).Equals(".dll", StringComparison.OrdinalIgnoreCase)) {
            Arguments.Insert(0, Program);
            info.Program = "dotnet";
        }

        return info;
    }

    private void OverrideFromLaunchSettings(string launchSettingsPath, string? profileName) {
        var settings = SafeExtensions.Invoke(() => JsonSerializer.Deserialize<LaunchSettings>(File.OpenRead(launchSettingsPath), SerializationExtensions.Options));
        if (settings?.Profiles == null || settings.Profiles.Count == 0)
            return;

        LaunchProfile? profile = null;
        if (!string.IsNullOrEmpty(profileName) && settings.Profiles.TryGetValue(profileName, out LaunchProfile? value))
            profile = value;
        if (profile == null && settings.Profiles.TryGetValue("https", out LaunchProfile? value2))
            profile = value2;
        if (profile == null)
            profile = settings.Profiles.Values.First();

        if (!string.IsNullOrEmpty(profile.ExecutablePath))
            Program = profile.ExecutablePath.ToPlatformPath();
        if (!string.IsNullOrEmpty(profile.workingDirectory))
            WorkingDirectory = profile.workingDirectory.ToPlatformPath();
        if (!string.IsNullOrEmpty(profile.CommandLineArgs))
            Arguments = profile.CommandLineArgs.Split(' ').ToList(); //TODO: update split!!!
        if (profile.EnvironmentVariables != null)
            EnvironmentVariables = profile.EnvironmentVariables;
    }
}