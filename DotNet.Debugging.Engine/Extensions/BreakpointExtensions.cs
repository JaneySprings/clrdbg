namespace DotNet.Debugging.Engine.Extensions;

internal static class BreakpointExtensions {
    // '10' or '==10': break on the 10th hit, '>=10', '>10', '<=10', '<10', '%10': break every 10th hit
    public static bool MatchesHitCount(this string hitCondition, int hitCount) {
        var condition = hitCondition.Trim().AsSpan();
        if (condition.StartsWith(">="))
            return int.TryParse(condition.Slice(2), out var threshold) && hitCount >= threshold;
        if (condition.StartsWith("<="))
            return int.TryParse(condition.Slice(2), out var threshold) && hitCount <= threshold;
        if (condition.StartsWith("=="))
            return int.TryParse(condition.Slice(2), out var target) && hitCount == target;
        if (condition.StartsWith('>'))
            return int.TryParse(condition.Slice(1), out var threshold) && hitCount > threshold;
        if (condition.StartsWith('<'))
            return int.TryParse(condition.Slice(1), out var threshold) && hitCount < threshold;
        if (condition.StartsWith('%'))
            return int.TryParse(condition.Slice(1), out var modulo) && modulo > 0 && hitCount % modulo == 0;
        return int.TryParse(condition, out var count) && hitCount == count;
    }
}
