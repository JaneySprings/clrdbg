using DotNet.Debugging.CorApi.Models;
using DotNet.Debugging.Soft.Extensions;
using Newtonsoft.Json.Linq;

namespace DotNet.Debugging.Soft;

public class LaunchConfiguration {
    public string ProgramPath { get; init; }
    public string? WorkingDirectory { get; init; }

    public List<string> ProgramArguments { get; init; }
    public Dictionary<string, string> EnvironmentVariables { get; init; }
    public LaunchRequestConsoleType ConsoleType { get; init; }
    public bool StopAtEntry { get; init; }
    public bool JustMyCode { get; init; }
    public int ProcessId { get; init; }

    private bool SkipDebug { get; init; }

    public LaunchConfiguration(Dictionary<string, JToken> configurationProperties) {
        SkipDebug = configurationProperties.TryGetValue("skipDebug").ToValue<bool>();
        StopAtEntry = configurationProperties.TryGetValue("stopAtEntry").ToValue<bool>();
        JustMyCode = configurationProperties.TryGetValue("justMyCode")?.ToValue<bool>() ?? true;
        ProcessId = configurationProperties.TryGetValue("processId").ToValue<int>();
        ConsoleType = GetConsoleType(configurationProperties.TryGetValue("console").ToClass<string>());
        WorkingDirectory = configurationProperties.TryGetValue("cwd").ToClass<string>();
        ProgramArguments = configurationProperties.TryGetValue("args")?.ToClass<List<string>>()
            ?? new List<string>();
        EnvironmentVariables = configurationProperties.TryGetValue("env")?.ToClass<Dictionary<string, string>>()
            ?? new Dictionary<string, string>();

        var programPath = configurationProperties.TryGetValue("program").ToClass<string>();
        ProgramPath = Path.GetFullPath(programPath);
        // The program is not required when attaching to an existing process
        if (ProcessId == 0 && !File.Exists(ProgramPath))
            throw ServerExtensions.GetProtocolException($"Incorrect path to program: '{programPath}'");
    }

    public BaseLaunchAgent GetLaunchAgent() {
        if (ProcessId != 0)
            return new AttachLaunchAgent(this);
        if (!SkipDebug)
            return new DebugLaunchAgent(this);

        return new NoDebugLaunchAgent(this);
    }
    public LaunchInfo GetLaunchInfo() {
        var program = ProgramPath;
        var arguments = new List<string>(ProgramArguments);
        if (Path.GetExtension(program).Equals(".dll", StringComparison.OrdinalIgnoreCase)) {
            arguments.Insert(0, program);
            program = "dotnet";
        }

        return new LaunchInfo {
            Program = program,
            Arguments = arguments,
            Cwd = WorkingDirectory ?? Path.GetDirectoryName(ProgramPath),
            Env = EnvironmentVariables,
            StopAtEntry = StopAtEntry,
            LaunchRequestConsoleType = ConsoleType
        };
    }

    private static LaunchRequestConsoleType GetConsoleType(string? console) {
        return console switch {
            null or "" or "internalConsole" => LaunchRequestConsoleType.InternalConsole,
            "integratedTerminal" => LaunchRequestConsoleType.IntegratedTerminal,
            "externalTerminal" => LaunchRequestConsoleType.ExternalTerminal,
            _ => throw ServerExtensions.GetProtocolException($"Invalid console type: '{console}'")
        };
    }
}
