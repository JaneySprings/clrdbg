namespace DotNet.Debugging.Engine.Models;

internal class AwaitInfo {
    public uint YieldOffset { get; }
    public uint ResumeOffset { get; }

    public AwaitInfo(uint yieldOffset, uint resumeOffset) {
        YieldOffset = yieldOffset;
        ResumeOffset = resumeOffset;
    }
}

internal class AsyncMethodInfo {
    public List<AwaitInfo> Awaits { get; }
    public int LastUserCodeOffset { get; set; }

    public AsyncMethodInfo() {
        Awaits = new List<AwaitInfo>();
    }
}
