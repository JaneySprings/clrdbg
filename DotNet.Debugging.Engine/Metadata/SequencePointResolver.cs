using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DotNet.Debugging.Engine.Metadata;

internal class SequencePointMatch {
    public int MethodToken { get; }
    public SequencePoint Point { get; }

    public SequencePointMatch(int methodToken, SequencePoint point) {
        MethodToken = methodToken;
        Point = point;
    }
}

// Finds the sequence point a source position maps to, snapping to the next line with code
// and choosing between a lambda and its enclosing method the way netcoredbg does
internal static class SequencePointResolver {
    public static SequencePointMatch? Resolve(MetadataReader pdbReader, DocumentHandle documentHandle, int line, int? column) {
        var candidates = new List<MethodCandidate>();
        foreach (var handle in pdbReader.MethodDebugInformation) {
            var candidate = CollectCandidate(pdbReader, handle, documentHandle, line, column);
            if (candidate != null)
                candidates.Add(candidate);
        }

        var covering = candidates.Where(it => it.Covering != null).ToList();
        if (covering.Count == 0) {
            // No method covers the position, snap to the closest following sequence point
            var closest = candidates.OrderBy(it => it.First, StartComparer.Instance).FirstOrDefault();
            return closest == null ? null : new SequencePointMatch(closest.MethodToken, closest.First);
        }
        if (covering.Count == 1)
            return new SequencePointMatch(covering[0].MethodToken, covering[0].Covering!.Value);

        // Keep only the candidates whose covering sequence point starts latest - i.e. whose sequence point most
        // specifically matches the target line. This naturally picks the innermost lambda when the enclosing
        // method only covers the line via a large spanning sequence point (e.g. a delegate-assignment point
        // that spans the whole lambda body)
        var latestStart = covering.Select(it => it.Covering!.Value).OrderByDescending(it => it, StartComparer.Instance).First();
        var primary = covering.Where(it => CompareStart(it.Covering!.Value, latestStart) == 0).ToList();
        if (primary.Count == 1)
            return new SequencePointMatch(primary[0].MethodToken, primary[0].Covering!.Value);

        // Several methods have a covering sequence point starting at the exact same position - the same-line
        // lambda case (e.g. items.Select(i => i * 2)). Apply netcoredbg's containment check for that case:
        // https://github.com/Samsung/netcoredbg/blob/8b8b22200fecdb1aec5f47af63215462d8c79a4b/src/managed/SymbolReader.cs#L801-L817
        var sorted = primary.OrderBy(it => it.First, StartComparer.Instance).ToList();
        var outer = sorted[^2];
        var nested = sorted[^1];

        // The lambda range is fully inside the outer's first sequence point - the breakpoint is on the call-site line
        if (CompareStart(nested.First, outer.First) > 0 && CompareEnd(nested.Last, outer.First) < 0)
            return new SequencePointMatch(outer.MethodToken, outer.Covering!.Value);
        // The outer's first sequence point ends after the nested one - the breakpoint is closer to the lambda body
        if (CompareEnd(outer.First, nested.First) > 0)
            return new SequencePointMatch(nested.MethodToken, nested.Covering!.Value);

        return new SequencePointMatch(outer.MethodToken, outer.Covering!.Value);
    }

    private static MethodCandidate? CollectCandidate(MetadataReader pdbReader, MethodDebugInformationHandle handle, DocumentHandle documentHandle, int line, int? column) {
        var debugInformation = pdbReader.GetMethodDebugInformation(handle);
        if (debugInformation.SequencePointsBlob.IsNil)
            return null;

        MethodCandidate? candidate = null;
        foreach (var point in debugInformation.GetSequencePoints()) {
            if (point.IsHidden)
                continue;
            var pointDocument = point.Document.IsNil ? debugInformation.Document : point.Document;
            if (pointDocument != documentHandle || IsBeforeRequestedPosition(point, line, column))
                continue;

            if (candidate == null)
                candidate = new MethodCandidate(MetadataTokens.GetToken(handle.ToDefinitionHandle()), point);

            if (CompareEnd(point, candidate.First) < 0)
                candidate.First = point;
            if (CompareEnd(point, candidate.Last) > 0)
                candidate.Last = point;
            if (CoversRequestedPosition(point, line, column) && ShouldReplaceCovering(point, candidate.Covering, column))
                candidate.Covering = point;
        }
        return candidate;
    }

    private static bool IsBeforeRequestedPosition(SequencePoint point, int line, int? column) {
        if (column == null)
            return point.EndLine < line;
        return ComparePosition(point.EndLine, point.EndColumn, line, column.Value) < 0;
    }
    private static bool CoversRequestedPosition(SequencePoint point, int line, int? column) {
        if (column == null)
            return point.StartLine <= line;
        return ComparePosition(point.StartLine, point.StartColumn, line, column.Value) <= 0
            && ComparePosition(line, column.Value, point.EndLine, point.EndColumn) <= 0;
    }
    private static bool ShouldReplaceCovering(SequencePoint point, SequencePoint? covering, int? column) {
        if (covering == null)
            return true;
        if (column == null) {
            return point.StartLine > covering.Value.StartLine
                || (point.StartLine == covering.Value.StartLine && point.StartColumn < covering.Value.StartColumn);
        }
        return CompareStart(point, covering.Value) > 0;
    }

    private static int CompareStart(SequencePoint left, SequencePoint right) {
        return ComparePosition(left.StartLine, left.StartColumn, right.StartLine, right.StartColumn);
    }
    private static int CompareEnd(SequencePoint left, SequencePoint right) {
        return ComparePosition(left.EndLine, left.EndColumn, right.EndLine, right.EndColumn);
    }
    private static int ComparePosition(int line, int column, int otherLine, int otherColumn) {
        var result = line.CompareTo(otherLine);
        return result != 0 ? result : column.CompareTo(otherColumn);
    }

    private class MethodCandidate {
        public int MethodToken { get; }
        // Smallest end at or after the requested position (next-line snapping)
        public SequencePoint First { get; set; }
        // Largest end at or after the requested position
        public SequencePoint Last { get; set; }
        // Latest start that covers the requested position
        public SequencePoint? Covering { get; set; }

        public MethodCandidate(int methodToken, SequencePoint point) {
            MethodToken = methodToken;
            First = point;
            Last = point;
        }
    }

    private class StartComparer : IComparer<SequencePoint> {
        public static StartComparer Instance { get; } = new StartComparer();

        public int Compare(SequencePoint left, SequencePoint right) {
            return CompareStart(left, right);
        }
    }
}
