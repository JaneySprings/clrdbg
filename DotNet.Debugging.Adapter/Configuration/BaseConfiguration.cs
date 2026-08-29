using DotNet.Debugging.Adapter.Extensions;
using Newtonsoft.Json.Linq;

namespace DotNet.Debugging.Adapter;

public abstract class BaseConfiguration {
    public bool JustMyCode { get; }
    public bool RequireExactSource { get; }
    public bool EnableStepFiltering { get; }
    public Dictionary<string, SourceLinkOptions> SourceLinkOptions { get; }
    public Dictionary<string, string> SourceFileMap { get; }
    public SymbolOptions SymbolOptions { get; }
    public LoggingOptions? Logging { get; }

    protected BaseConfiguration(Dictionary<string, JToken> properties) {
        JustMyCode = properties.TryGetValue("justMyCode").ToValue<bool>(true);
        RequireExactSource = properties.TryGetValue("requireExactSource").ToValue<bool>(true);
        EnableStepFiltering = properties.TryGetValue("enableStepFiltering").ToValue<bool>(true);
        Logging = properties.TryGetValue("logging").ToClass<LoggingOptions>();
        SymbolOptions = properties.TryGetValue("symbolOptions").ToClass<SymbolOptions>() ?? new SymbolOptions();
        SourceFileMap = properties.TryGetValue("sourceFileMap").ToClass<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        SourceLinkOptions = properties.TryGetValue("sourceLinkOptions").ToClass<Dictionary<string, SourceLinkOptions>>()
            ?? new Dictionary<string, SourceLinkOptions> { ["*"] = new SourceLinkOptions() };
    }

    public abstract IDebugAgent CreateDebugAgent(DebugSession debugSession);
    public abstract string GetApplicationName();
    public abstract void VerifyMissingProperties();
}
