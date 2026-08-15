using DotNet.Debugging.CorApi.Models;
using DotNet.Debugging.Soft.Extensions;
using Newtonsoft.Json.Linq;

namespace DotNet.Debugging.Soft;

public class LaunchConfiguration : BaseConfiguration {
    public string? Program { get; }
    public List<string> Arguments { get; }
    public string? WorkingDirectory { get; }
    public Dictionary<string, string> EnvironmentVariables { get; }
    public LaunchRequestConsoleType Console { get; }
    public bool SuppressJITOptimizations { get; }
    public bool StopAtEntry { get; }
    public string? LaunchSettingsFilePath { get; }
    public string? LaunchSettingsProfile { get; }
    // TODO: implement
    public object? PipeTransport { get; }

    public LaunchConfiguration(Dictionary<string, JToken> properties) : base(properties) {
        Program = properties.TryGetValue("program").ToClass<string>();
        Arguments = properties.TryGetValue("args").ToClass<List<string>>() ?? new List<string>();
        WorkingDirectory = properties.TryGetValue("cwd").ToClass<string>();
        EnvironmentVariables = properties.TryGetValue("env").ToClass<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        Console = properties.TryGetValue("console").ToValue<LaunchRequestConsoleType>(LaunchRequestConsoleType.InternalConsole);
        SuppressJITOptimizations = properties.TryGetValue("suppressJITOptimizations").ToValue<bool>(false);
        StopAtEntry = properties.TryGetValue("stopAtEntry").ToValue<bool>(false);
        LaunchSettingsFilePath = properties.TryGetValue("launchSettingsFilePath").ToClass<string>();
        LaunchSettingsProfile = properties.TryGetValue("launchSettingsProfile").ToClass<string>();
    }

    public override IDebugAgent CreateDebugAgent() {
        if (SkipDebug)
            return new SkipDebugAgent(this);
        return new LaunchDebugAgent(this);
    }
    public override void VerifyMissingProperties() {
        if (string.IsNullOrEmpty(Program) || (!File.Exists(Program) && !Directory.Exists(Program)))
            throw ServerExtensions.GetProtocolException(string.Format(Resources.MessageInvalidProgram, Program));
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
}