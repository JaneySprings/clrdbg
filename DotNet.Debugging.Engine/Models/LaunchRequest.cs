using DotNet.Debugging.Engine.Enums;

namespace DotNet.Debugging.Engine.Models;

public class LaunchRequest {
    public string Program { get; set; }
    public string? WorkingDirectory { get; set; }
    public List<string> Arguments { get; set; }
    public Dictionary<string, string> Environment { get; set; }
    public bool StopAtEntry { get; set; }
    public ConsoleType Console { get; set; }
    // Set by the 'OnTerminalLaunchRequested' subscriber: the id of the process it started in the terminal
    public int? ProcessId { get; set; }

    public LaunchRequest(string program) {
        Program = program;
        Arguments = new List<string>();
        Environment = new Dictionary<string, string>();
    }
}
