using DotNet.Debugging.Engine.Enums;

namespace DotNet.Debugging.Engine.Models;

public class StackFrameInfo {
    public int Id { get; }
    public StackFrameKind Kind { get; }
    // 'Namespace.Type.Method(string[] args)' for managed frames, a description like 'Managed to Native Transition' for the others
    public string Name { get; }
    public string? ModuleName { get; set; }
    public string? ModulePath { get; set; }
    public SourceLocation? Location { get; set; }
    // The native address the frame is executing at, null when the code is not jitted or the frame is not managed
    public ulong? InstructionPointer { get; set; }

    public StackFrameInfo(int id, StackFrameKind kind, string name) {
        Id = id;
        Kind = kind;
        Name = name;
    }
}
