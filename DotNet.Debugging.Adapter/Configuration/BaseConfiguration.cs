using DotNet.Debugging.Adapter.Extensions;
using Newtonsoft.Json.Linq;

namespace DotNet.Debugging.Adapter;

public abstract class BaseConfiguration {
    public bool SkipDebug { get; }
    public bool JustMyCode { get; }
    public bool RequireExactSource { get; }
    public bool EnableStepFiltering { get; }
    public Dictionary<string, SourceLinkOptions> SourceLinkOptions { get; }
    // TODO: implement
    public object? SourceFileMap { get; }
    public object? SymbolOptions { get; }

    protected BaseConfiguration(Dictionary<string, JToken> properties) {
        SkipDebug = properties.TryGetValue("skipDebug").ToValue<bool>(false);
        JustMyCode = properties.TryGetValue("justMyCode").ToValue<bool>(true);
        RequireExactSource = properties.TryGetValue("requireExactSource").ToValue<bool>(true);
        EnableStepFiltering = properties.TryGetValue("enableStepFiltering").ToValue<bool>(false);
        SourceLinkOptions = properties.TryGetValue("sourceLinkOptions").ToClass<Dictionary<string, SourceLinkOptions>>()
            ?? new Dictionary<string, SourceLinkOptions> { ["*"] = new SourceLinkOptions() };
    }

    public abstract IDebugAgent CreateDebugAgent(DebugSession debugSession);
    public abstract string GetApplicationName();
    public abstract void VerifyMissingProperties();
}
