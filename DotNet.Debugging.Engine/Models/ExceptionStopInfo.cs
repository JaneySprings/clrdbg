using DotNet.Debugging.Engine.Enums;

namespace DotNet.Debugging.Engine.Models;

public class ExceptionStopInfo {
    public int ThreadId { get; }
    public ExceptionStopKind Kind { get; }
    public string? TypeName { get; }
    // The name of the module the exception is attributed to, shown in "Exception thrown: '...' in <module>"
    public string? ModuleName { get; }

    public ExceptionStopInfo(int threadId, ExceptionStopKind kind, string? typeName, string? moduleName) {
        ThreadId = threadId;
        Kind = kind;
        TypeName = typeName;
        ModuleName = moduleName;
    }
}
