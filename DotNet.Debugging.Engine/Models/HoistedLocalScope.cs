namespace DotNet.Debugging.Engine.Models;

// The IL range of a state machine's MoveNext in which one of its hoisted locals is in scope
internal class HoistedLocalScope {
    public int StartOffset { get; }
    public int EndOffset { get; }

    public HoistedLocalScope(int startOffset, int length) {
        StartOffset = startOffset;
        EndOffset = startOffset + length;
    }

    public bool Contains(int ilOffset) {
        return ilOffset >= StartOffset && ilOffset < EndOffset;
    }
}
