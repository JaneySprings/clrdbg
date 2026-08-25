using System.Text.Json;
using DotNet.Debugging.Adapter.Extensions;
using DotNet.Debugging.Common.Extensions;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Models;
using Newtonsoft.Json.Linq;

namespace DotNet.Debugging.Adapter;

public class LaunchConfiguration : BaseConfiguration {
    public string Program { get; private set; }
    public string? WorkingDirectory { get; private set; }
    public List<string> Arguments { get; private set; }
    public Dictionary<string, string> EnvironmentVariables { get; private set; }
    public ConsoleType Console { get; }
    public bool SuppressJITOptimizations { get; }
    public bool StopAtEntry { get; }
    public string? LaunchSettingsFilePath { get; }
    public string? LaunchSettingsProfile { get; }
    public CoreClrMobileDebuggerOptions? MobileOptions { get; }
    public string? RemoteHostDirectory { get; }
    public string? RemoteTargetDirectory { get; }
    // TODO: implement
    public object? PipeTransport { get; }

    public LaunchConfiguration(Dictionary<string, JToken> properties) : base(properties) {
        Program = properties.TryGetValue("program").ToClass<string>().ToPlatformPath();
        WorkingDirectory = properties.TryGetValue("cwd").ToClass<string>().ToPlatformPath();
        Arguments = properties.TryGetValue("args").ToClass<List<string>>() ?? new List<string>();
        EnvironmentVariables = properties.TryGetValue("env").ToClass<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        Console = properties.TryGetValue("console").ToValue<ConsoleType>(ConsoleType.InternalConsole);
        SuppressJITOptimizations = properties.TryGetValue("suppressJITOptimizations").ToValue<bool>(false);
        StopAtEntry = properties.TryGetValue("stopAtEntry").ToValue<bool>(false);
        LaunchSettingsFilePath = properties.TryGetValue("launchSettingsFilePath").ToClass<string>().ToPlatformPath();
        LaunchSettingsProfile = properties.TryGetValue("launchSettingsProfile").ToClass<string>();
        MobileOptions = properties.TryGetValue("coreClrMobileDebuggerOptions").ToClass<CoreClrMobileDebuggerOptions>();
        RemoteHostDirectory = properties.TryGetValue("remoteCoreclrHost").ToClass<string>().ToPlatformPath();
        RemoteTargetDirectory = properties.TryGetValue("remoteCoreclrTarget").ToClass<string>().ToPlatformPath();

        if (string.IsNullOrEmpty(WorkingDirectory))
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(Program));
        if (!string.IsNullOrEmpty(WorkingDirectory)) {
            if (!Path.IsPathRooted(Program))
                Program = Path.Combine(WorkingDirectory, Program);
            if (!Path.IsPathRooted(LaunchSettingsFilePath))
                LaunchSettingsFilePath = Path.Combine(WorkingDirectory, LaunchSettingsFilePath);
        }
        if (!string.IsNullOrEmpty(MobileOptions?.AssetsPath))
            MobileOptions.AssetsPath = MobileOptions.AssetsPath.ToPlatformPath();

        if (File.Exists(LaunchSettingsFilePath))
            OverrideFromLaunchSettings(LaunchSettingsFilePath, LaunchSettingsProfile);
    }

    public override IDebugAgent CreateDebugAgent(DebugSession debugSession) {
        if (MobileOptions != null)
            return new MobileDebugAgent(this, debugSession);
        return new LaunchDebugAgent(this, debugSession);
    }
    public override string GetApplicationName() {
        if (MobileOptions == null)
            return Path.GetFileName(Program);
        return Path.GetFileNameWithoutExtension(Program).Replace("-Signed", string.Empty);
    }
    public override void VerifyMissingProperties() {
        if (string.IsNullOrEmpty(Program) || (!File.Exists(Program) && !Directory.Exists(Program)))
            throw new ArgumentException(string.Format(Resources.MsgInvalidProgram, Program));

        if (MobileOptions != null) {
            if (string.IsNullOrEmpty(MobileOptions.AssetsPath))
                throw new ArgumentException(Resources.MsgMissingAssets);
            if (string.IsNullOrEmpty(RemoteHostDirectory) || !Directory.Exists(RemoteHostDirectory))
                throw new ArgumentException(Resources.MsgMissingCoreclrHost);
            if (string.IsNullOrEmpty(RemoteTargetDirectory) || !Directory.Exists(RemoteTargetDirectory))
                throw new ArgumentException(Resources.MsgMissingCoreclrTarget);
        }
    }

    public LaunchInfo GetLaunchInfo() {
        ArgumentNullException.ThrowIfNull(Program);
        var info = new LaunchInfo(Program);
        info.Arguments = Arguments;
        info.WorkingDirectory = WorkingDirectory ?? Path.GetDirectoryName(Program);
        info.Environment = EnvironmentVariables;
        info.StopAtEntry = StopAtEntry;
        info.Console = Console;

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