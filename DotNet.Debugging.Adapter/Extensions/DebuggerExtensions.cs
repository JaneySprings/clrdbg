using System.Text;
using System.Text.RegularExpressions;
using DotNet.Debugging.Common.Logging;
using DotNet.Debugging.Engine;

namespace DotNet.Debugging.Adapter.Extensions;

public static partial class DebuggerExtensions {
    public static string ToDisplayName(this string? variableName, string? typeName) {
        if (string.IsNullOrEmpty(variableName) || string.IsNullOrEmpty(typeName))
            return variableName ?? string.Empty;

        return $"{variableName} [{ToShortTypeName(typeName)}]";
    }
    public static string ToVariableName(this string displayName) {
        if (!displayName.EndsWith(']'))
            return displayName;

        var suffixIndex = displayName.LastIndexOf(" [", StringComparison.Ordinal);
        return suffixIndex <= 0 ? displayName : displayName.Substring(0, suffixIndex);
    }
    private static string ToShortTypeName(string typeName) {
        var result = new StringBuilder(typeName.Length);
        var segmentStart = 0;
        for (var i = 0; i <= typeName.Length; i++) {
            if (i < typeName.Length && typeName[i] is not ('<' or '>' or ',' or ' '))
                continue;

            var segment = typeName.AsSpan(segmentStart, i - segmentStart);
            result.Append(segment[(segment.LastIndexOf('.') + 1)..]);
            if (i < typeName.Length)
                result.Append(typeName[i]);
            segmentStart = i + 1;
        }
        return result.ToString();
    }

    public static string ToThreadName(this string? threadName, int threadId) {
        return string.IsNullOrEmpty(threadName) ? "<No Name>" : threadName;
    }
    public static string ToLoadedAssemblyMessage(this ModuleLoadedInfo moduleInfo, string processName, bool justMyCode) {
        var symbolStatus = Resources.MsgCannotFindPdb;
        if (moduleInfo.SymbolsLoaded)
            symbolStatus = Resources.MsgPdbLoaded;
        else if (moduleInfo.IsOptimized && justMyCode)
            symbolStatus = Resources.MsgPdfSkipped;

        return $"{processName} ({moduleInfo.ProcessId}): Loaded '{moduleInfo.ModulePath}'. {symbolStatus}";
    }
    public static string ToInterpolatedLogMessage(this ManagedDebugger debugger, string message, int threadId) {
        var result = LogpointExpressionRegex().Replace(message, match => {
            try {
                var frameId = debugger.GetTopFrameId(threadId);
                var variable = debugger.Evaluate(match.Groups[1].Value, frameId).GetAwaiter().GetResult();
                return variable.Value;
            }
            catch (Exception ex) {
                CurrentSessionLogger.Error($"[LogPoint] Failed to evaluate '{match.Groups[1].Value}' {ex}");
                return match.Value;
            }
        });
        return $"[LogPoint]: {result}";
    }
    [GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.Compiled)]
    private static partial Regex LogpointExpressionRegex();
}