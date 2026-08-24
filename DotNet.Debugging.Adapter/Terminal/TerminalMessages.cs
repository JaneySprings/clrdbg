namespace DotNet.Debugging.Adapter.Terminal;

public class TerminalLaunchResponse {
    public int? ProcessId { get; set; }
    public string? Error { get; set; }
}

public class TerminalLaunchRequest {
    public string Program { get; set; }
    public List<string> Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> Environment { get; set; }

    public TerminalLaunchRequest() {
        Program = string.Empty;
        Arguments = new List<string>();
        Environment = new Dictionary<string, string>();
    }
}
