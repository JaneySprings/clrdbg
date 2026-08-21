namespace DotNet.Debugging.Engine.Models.Response;

public class StackFrameInfo {
    public required int Id { get; set; }
    public required string Name { get; set; }
    public required int Line { get; set; }
    public required int EndLine { get; set; }
    public required int Column { get; set; }
    public required int EndColumn { get; set; }
    public required string? Source { get; set; }
    public ModuleMetadataReader.SourceChecksum? SourceChecksum { get; set; }
    public string? ModulePath { get; set; }
    /// <summary>The native instruction pointer as '0x' + 16 hex digits, null when the code is not jitted or the frame is not managed</summary>
    public string? InstructionPointerReference { get; set; }
}

public record StackTraceInfo(List<StackFrameInfo> Frames, int TotalFrames);
