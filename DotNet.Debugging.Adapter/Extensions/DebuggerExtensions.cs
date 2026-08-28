using System.Text;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Models;

namespace DotNet.Debugging.Adapter.Extensions;

public static class DebuggerExtensions {
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
    // 'Module.dll!Namespace.Type.Method(string[] args) Line 7', the line only when the source is known
    public static string ToDisplayName(this StackFrameInfo frame) {
        if (frame.Kind != StackFrameKind.Managed)
            return frame.Name;

        var name = $"{frame.ModuleName}!{frame.Name}";
        if (frame.Location != null)
            name += $" Line {frame.Location.Line}";
        return name;
    }
    public static string ToDisplayName(this ThreadInfo thread) {
        if (!string.IsNullOrEmpty(thread.Name))
            return thread.Name;
        return thread.IsMain ? "Main Thread" : "<No Name>";
    }
    // Microsoft's '1.00.0.0' form
    public static string? ToDisplayVersion(this Version? version) {
        if (version == null)
            return null;
        return $"{version.Major}.{Math.Max(version.Minor, 0):00}.{Math.Max(version.Build, 0)}.{Math.Max(version.Revision, 0)}";
    }
    public static string ToStatusMessage(this Breakpoint breakpoint) {
        return breakpoint.Status switch {
            BreakpointStatus.Pending => Resources.MsgBreakpointPending,
            BreakpointStatus.NotProcessed => Resources.MsgBreakpointNotProcessed,
            BreakpointStatus.NoSymbols => Resources.MsgBreakpointNoSymbols,
            BreakpointStatus.SourceMismatch => string.Format(Resources.MsgBreakpointSourceMismatch, Path.GetFileName(breakpoint.FilePath), breakpoint.SourceMismatchModule),
            BreakpointStatus.NoMatchingFunctions => string.Format(Resources.MsgBreakpointNoFunctions, breakpoint.FunctionName),
            BreakpointStatus.Error => string.Format(Resources.MsgBreakpointError, breakpoint.Error),
            _ => string.Empty
        };
    }
    public static string ToLoadedAssemblyMessage(this ModuleInfo module, string processName, int processId, bool justMyCode) {
        var symbolStatus = Resources.MsgCannotFindPdb;
        if (module.HasSymbols)
            symbolStatus = Resources.MsgPdbLoaded;
        else if (!module.IsUserCode && justMyCode)
            symbolStatus = Resources.MsgPdbSkipped;

        return $"{processName} ({processId}): Loaded '{module.Path}'. {symbolStatus}";
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
}
