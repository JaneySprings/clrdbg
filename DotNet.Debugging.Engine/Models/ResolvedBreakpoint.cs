namespace DotNet.Debugging.Engine.Models;

internal class ResolvedBreakpoint {
    public int MethodToken { get; }
    public int ILOffset { get; }
    public SourceLocation Location { get; }
    // The document was matched by full path or content checksum rather than by file name alone
    public bool IsExactMatch { get; }

    public ResolvedBreakpoint(int methodToken, int ilOffset, SourceLocation location, bool isExactMatch) {
        MethodToken = methodToken;
        ILOffset = ilOffset;
        Location = location;
        IsExactMatch = isExactMatch;
    }
}
