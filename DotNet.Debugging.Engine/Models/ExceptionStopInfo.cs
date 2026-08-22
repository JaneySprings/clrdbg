using DotNet.Debugging.Engine.Enums;

namespace DotNet.Debugging.Engine.Models;

public class ExceptionStopInfo {
    public int ThreadId { get; }
    public ExceptionStopKind Kind { get; }
    public string? TypeName { get; }

    public ExceptionStopInfo(int threadId, ExceptionStopKind kind, string? typeName) {
        ThreadId = threadId;
        Kind = kind;
        TypeName = typeName;
    }
}
