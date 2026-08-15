using DotNet.Debugging.Common.Extensions;
using DotNet.Debugging.Soft.Extensions;
using Newtonsoft.Json.Linq;

namespace DotNet.Debugging.Soft;

public class AttachConfiguration : BaseConfiguration {
    public int ProcessId { get; }
    public string? ProcessName { get; private set; }

    public AttachConfiguration(Dictionary<string, JToken> properties) : base(properties) {
        ProcessId = properties.TryGetValue("processId").ToValue<int>();
        ProcessName = properties.TryGetValue("processId").ToClass<string>();
    }

    public override IDebugAgent CreateDebugAgent(DebugSession debugSession) {
        return new AttachDebugAgent(this, debugSession);
    }
    public override string GetApplicationName() {
        if (string.IsNullOrEmpty(ProcessName))
            ProcessName = SafeExtensions.Invoke("dotnet", () => System.Diagnostics.Process.GetProcessById(ProcessId).ProcessName);
        return ProcessName;
    }
    public override void VerifyMissingProperties() {
        if (string.IsNullOrEmpty(ProcessName) && ProcessId <= 0)
            throw ServerExtensions.GetProtocolException(Resources.MessageMissingProcess);
    }
}