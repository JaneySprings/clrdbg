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
    // 'Module.dll!Namespace.Type.Method(string[] args)'
    public static string ToDisplayName(this StackFrameInfo frame) {
        if (frame.Kind != StackFrameKind.Managed)
            return frame.Name;
        return $"{frame.ModuleName}!{frame.Name}";
    }
    public static string ToDisplayName(this ThreadInfo thread) {
        if (!string.IsNullOrEmpty(thread.Name))
            return thread.Name;
        return thread.IsMain ? "Main Thread" : "<No Name>";
    }
    public static string? ToStatusMessage(this Breakpoint breakpoint) {
        return breakpoint.Status switch {
            BreakpointStatus.Unbound => breakpoint.IsFunctionBreakpoint ? string.Format(Resources.MsgFunctionBreakpointUnbound, breakpoint.FunctionName) : Resources.MsgBreakpointUnbound,
            BreakpointStatus.SourceMismatch => Resources.MsgBreakpointSourceMismatch,
            BreakpointStatus.Error => string.Format(Resources.MsgBreakpointError, breakpoint.Error),
            _ => null
        };
    }
    public static string ToLoadedAssemblyMessage(this ModuleInfo module, string processName, int processId, bool justMyCode) {
        return $"{processName} ({processId}): Loaded '{module.Path}'. {module.ToSymbolStatus(justMyCode, detailed: true)}";
    }
    // The detailed form explains a skip in the console line, the module event carries the short one
    public static string ToSymbolStatus(this ModuleInfo module, bool justMyCode, bool detailed = false) {
        if (module.HasSymbols)
            return Resources.MsgPdbLoaded;
        if (!module.IsUserCode && justMyCode)
            return detailed ? Resources.MsgPdbSkipped : Resources.MsgPdbSkippedShort;
        return Resources.MsgCannotFindPdb;
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
