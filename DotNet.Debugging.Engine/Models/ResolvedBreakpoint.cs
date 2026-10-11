using DotNet.Debugging.Engine.Enums;

namespace DotNet.Debugging.Engine.Models;

internal class ResolvedBreakpoint {
    public int MethodToken { get; }
    public int ILOffset { get; }
    public SourceLocation Location { get; }
    public DocumentMatchKind MatchKind { get; }

    public ResolvedBreakpoint(int methodToken, int ilOffset, SourceLocation location, DocumentMatchKind matchKind) {
        MethodToken = methodToken;
        ILOffset = ilOffset;
        Location = location;
        MatchKind = matchKind;
    }
}
