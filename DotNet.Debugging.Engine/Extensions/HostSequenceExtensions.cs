using DotNet.Debugging.Engine.Evaluation;

namespace DotNet.Debugging.Engine.Extensions;

internal static class HostSequenceExtensions {
    // A stable sort by every ordering in turn: the index breaks the ties, so equal keys keep their order
    public static void Sort(this HostSequence sequence) {
        var order = Enumerable.Range(0, sequence.Items.Count).ToList();
        order.Sort((left, right) => {
            foreach (var ordering in sequence.Orderings) {
                var result = ordering.Keys[left].CompareValue(ordering.Keys[right], ordering.IsUnsigned);
                if (ordering.Descending)
                    result = -result;
                if (result != 0)
                    return result;
            }
            return left.CompareTo(right);
        });
        Reorder(sequence.Items, order);
        foreach (var ordering in sequence.Orderings)
            Reorder(ordering.Keys, order);
    }

    private static void Reorder(List<CilValue> values, List<int> order) {
        var reordered = order.Select(it => values[it]).ToList();
        values.Clear();
        values.AddRange(reordered);
    }
}
