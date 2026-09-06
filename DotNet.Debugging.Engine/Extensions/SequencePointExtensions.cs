using System.Reflection.Metadata;

namespace DotNet.Debugging.Engine.Extensions;

internal static class SequencePointExtensions {
    public static int CompareStart(this SequencePoint left, SequencePoint right) {
        return (left.StartLine, left.StartColumn).CompareTo((right.StartLine, right.StartColumn));
    }
    public static int CompareEnd(this SequencePoint left, SequencePoint right) {
        return (left.EndLine, left.EndColumn).CompareTo((right.EndLine, right.EndColumn));
    }
}
