using DotNet.Debugging.Engine.Enums;

namespace DotNet.Debugging.Engine.Models;

public class StopInfo {
    public int ThreadId { get; }
    public StopReason Reason { get; }
    public SourceLocation? Location { get; }
    public List<int>? HitBreakpointIds { get; }

    public StopInfo(int threadId, StopReason reason, SourceLocation? location = null, List<int>? hitBreakpointIds = null) {
        ThreadId = threadId;
        Reason = reason;
        Location = location;
        HitBreakpointIds = hitBreakpointIds;
    }
}
