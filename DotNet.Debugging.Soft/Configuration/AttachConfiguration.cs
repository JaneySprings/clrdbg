using DotNet.Debugging.Soft.Extensions;
using Newtonsoft.Json.Linq;

namespace DotNet.Debugging.Soft;

public class AttachConfiguration : BaseConfiguration {
    public int ProcessId { get; }
    public string? ProcessName { get; }

    public AttachConfiguration(Dictionary<string, JToken> properties) : base(properties) {
        ProcessId = properties.TryGetValue("processId").ToValue<int>();
        ProcessName = properties.TryGetValue("processId").ToClass<string>();
    }

    public override IDebugAgent CreateDebugAgent() {
        return new AttachDebugAgent(this);
    }
    public override void VerifyMissingProperties() {
        if (string.IsNullOrEmpty(ProcessName) && ProcessId <= 0)
            throw ServerExtensions.GetProtocolException(Resources.MessageMissingProcess);
    }
}