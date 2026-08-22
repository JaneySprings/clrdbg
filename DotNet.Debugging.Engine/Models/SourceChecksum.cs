namespace DotNet.Debugging.Engine.Models;

public class SourceChecksum {
    // 'SHA1' or 'SHA256'
    public string Algorithm { get; }
    // Lowercase hex
    public string Value { get; }

    public SourceChecksum(string algorithm, string value) {
        Algorithm = algorithm;
        Value = value;
    }
}
