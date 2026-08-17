using System.Text.RegularExpressions;
using DotNet.Debugging.Common.Logging;
using DotNet.Debugging.CorApi;

namespace DotNet.Debugging.Soft.Extensions;

public static partial class DebuggerExtensions {
    private static readonly HashSet<string> simpleTypeNames = new HashSet<string> {
        "bool", "byte", "sbyte", "char", "short", "ushort", "int", "uint",
        "long", "ulong", "float", "double", "decimal", "string", "nint", "nuint"
    };

    public static string ToDisplayName(this string? variableName, string? typeName) {
        if (string.IsNullOrEmpty(variableName) || string.IsNullOrEmpty(typeName))
            return variableName ?? string.Empty;
        if (!IsSimpleType(typeName))
            return variableName;

        return $"{variableName} [{typeName}]";
    }
    public static string ToVariableName(this string displayName) {
        // Clients send the display name back in 'SetVariable' requests - strip the '[type]' suffix
        var suffixIndex = displayName.LastIndexOf(" [", StringComparison.Ordinal);
        if (suffixIndex <= 0 || !displayName.EndsWith(']'))
            return displayName;

        var typeName = displayName.Substring(suffixIndex + 2, displayName.Length - suffixIndex - 3);
        return IsSimpleType(typeName) ? displayName.Substring(0, suffixIndex) : displayName;
    }
    private static bool IsSimpleType(string typeName) {
        // 'int?' is a simple type as well
        if (typeName.EndsWith('?'))
            typeName = typeName.Substring(0, typeName.Length - 1);
        return simpleTypeNames.Contains(typeName);
    }

    public static string ToThreadName(this string? threadName, int threadId) {
        if (!string.IsNullOrEmpty(threadName))
            return threadName;
        return "<No Name>";
    }
    public static string ToLoadedAssemblyMessage(this ModuleLoadedInfo moduleInfo, string processName, bool justMyCode) {
        var symbolStatus = Resources.MessageCannotFindPdb;
        if (moduleInfo.SymbolsLoaded)
            symbolStatus = Resources.MessagePdbLoaded;
        else if (moduleInfo.IsOptimized && justMyCode)
            symbolStatus = Resources.MessagePdfSkipped;

        return $"dotnet ({moduleInfo.ProcessId}): Loaded '{moduleInfo.ModulePath}'. {symbolStatus}";
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