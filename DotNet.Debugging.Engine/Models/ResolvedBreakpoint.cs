namespace DotNet.Debugging.Engine.Models;

internal class ResolvedBreakpoint {
    public int MethodToken { get; }
    public int ILOffset { get; }
    public SourceLocation Location { get; }

    public ResolvedBreakpoint(int methodToken, int ilOffset, SourceLocation location) {
        MethodToken = methodToken;
        ILOffset = ilOffset;
        Location = location;
    }
}
