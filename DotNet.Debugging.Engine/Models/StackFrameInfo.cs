using DotNet.Debugging.Engine.Enums;

namespace DotNet.Debugging.Engine.Models;

public class StackFrameInfo {
    public int Id { get; }
    public StackFrameKind Kind { get; }
    // 'Namespace.Type.Method(string[] args)' for managed frames, a description like 'Managed to Native Transition' for the others
    public string Name { get; }
    public string? ModuleName { get; set; }
    // The id of the module the frame's method belongs to, the one its module event carried
    public int? ModuleId { get; set; }
    public SourceLocation? Location { get; set; }

    public StackFrameInfo(int id, StackFrameKind kind, string name) {
        Id = id;
        Kind = kind;
        Name = name;
    }
}
