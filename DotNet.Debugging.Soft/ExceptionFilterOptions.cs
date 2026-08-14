namespace DotNet.Debugging.Soft;

public class ExceptionFilterOptions {
    public bool Enabled { get; private set; }
    private readonly List<string> includedTypes = new List<string>();
    private readonly List<string> excludedTypes = new List<string>();

    public void Reset() {
        Enabled = false;
        includedTypes.Clear();
        excludedTypes.Clear();
    }
    public void Enable(string? condition = null) {
        Enabled = true;
        if (string.IsNullOrEmpty(condition))
            return;

        if (condition.StartsWith('!')) {
            foreach (var exceptionType in condition.Substring(1).Split(',', StringSplitOptions.RemoveEmptyEntries))
                excludedTypes.Add(exceptionType.Trim());
        }
        else {
            foreach (var exceptionType in condition.Split(',', StringSplitOptions.RemoveEmptyEntries))
                includedTypes.Add(exceptionType.Trim());
        }
    }
    public bool ShouldStopOnException(string? typeName) {
        if (!Enabled)
            return false;
        if (string.IsNullOrEmpty(typeName))
            return true;
        if (includedTypes.Count > 0 && !includedTypes.Contains(typeName))
            return false;
        if (excludedTypes.Contains(typeName))
            return false;

        return true;
    }
}
