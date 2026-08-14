using System.Text.RegularExpressions;
using DotNet.Debugging.Common.Logging;
using DotNet.Debugging.CorApi;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using CorDebugResponse = DotNet.Debugging.CorApi.Models.Response;
using DebugProtocol = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

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
    public static string ToProcessName(this int processId) {
        try {
            return System.Diagnostics.Process.GetProcessById(processId).ProcessName;
        }
        catch {
            return "dotnet";
        }
    }
    public static string ToLoadedAssemblyMessage(this ModuleLoadedInfo moduleInfo, string processName, bool justMyCode) {
        var symbolStatus = "Cannot find or open the PDB file.";
        if (moduleInfo.SymbolsLoaded)
            symbolStatus = "Symbols loaded.";
        else if (moduleInfo.IsOptimized && justMyCode)
            symbolStatus = "Skipped loading symbols. Module is optimized and the debugger option 'Just My Code' is enabled.";

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

    public static StoppedEvent.ReasonValue ToStoppedReason(this string reason) {
        return reason.ToLowerInvariant() switch {
            "step" => StoppedEvent.ReasonValue.Step,
            "breakpoint" => StoppedEvent.ReasonValue.Breakpoint,
            "exception" => StoppedEvent.ReasonValue.Exception,
            "pause" => StoppedEvent.ReasonValue.Pause,
            "entry" => StoppedEvent.ReasonValue.Entry,
            "goto" => StoppedEvent.ReasonValue.Goto,
            "function breakpoint" => StoppedEvent.ReasonValue.FunctionBreakpoint,
            "data breakpoint" => StoppedEvent.ReasonValue.DataBreakpoint,
            "instruction breakpoint" => StoppedEvent.ReasonValue.InstructionBreakpoint,
            _ => StoppedEvent.ReasonValue.Unknown
        };
    }

    public static DebugProtocol.Breakpoint ToBreakpoint(this BreakpointManager.BreakpointInfo breakpoint) {
        return new DebugProtocol.Breakpoint() {
            Id = breakpoint.Id,
            Verified = breakpoint.Verified,
            Message = breakpoint.Message,
            Line = breakpoint.IsFunctionBreakpoint ? null : breakpoint.Line,
            Column = breakpoint is { IsFunctionBreakpoint: false, Verified: true } ? breakpoint.Column : null,
            EndLine = breakpoint is { IsFunctionBreakpoint: false, Verified: true } ? breakpoint.EndLine : null,
            EndColumn = breakpoint is { IsFunctionBreakpoint: false, Verified: true } ? breakpoint.EndColumn : null,
            Source = breakpoint is not { IsFunctionBreakpoint: false, Verified: true } ? null : new DebugProtocol.Source {
                Path = breakpoint.FilePath,
                Name = Path.GetFileName(breakpoint.FilePath),
            }
        };
    }

    public static DebugProtocol.StackFrame ToStackFrame(this CorDebugResponse.StackFrameInfo frame) {
        DebugProtocol.Source? source = null;
        if (!string.IsNullOrEmpty(frame.Source)) {
            source = new DebugProtocol.Source() {
                Name = Path.GetFileName(frame.Source),
                Path = frame.Source,
            };
        }
        return new DebugProtocol.StackFrame() {
            Id = frame.Id,
            Source = source,
            Name = frame.Name,
            Line = frame.Line,
            Column = frame.Column,
            EndLine = frame.EndLine,
            EndColumn = frame.EndColumn,
            PresentationHint = DebugProtocol.StackFrame.PresentationHintValue.Normal
        };
    }

    public static DebugProtocol.Scope ToScope(this CorDebugResponse.ScopeInfo scope) {
        return new DebugProtocol.Scope() {
            Name = scope.Name,
            PresentationHint = scope.Name == "Locals" ? DebugProtocol.Scope.PresentationHintValue.Locals : DebugProtocol.Scope.PresentationHintValue.Unknown,
            VariablesReference = scope.VariablesReference,
            Expensive = scope.Expensive
        };
    }

    public static DebugProtocol.Variable ToVariable(this CorDebugResponse.VariableInfo variable) {
        return new DebugProtocol.Variable {
            Name = variable.Name.ToDisplayName(variable.Type),
            Type = variable.Type,
            Value = variable.Value,
            EvaluateName = variable.Name,
            PresentationHint = variable.PresentationHint?.ToDto(),
            VariablesReference = variable.VariablesReference
        };
    }

    public static DebugProtocol.ExceptionInfoResponse ToExceptionInfoResponse(this CorDebugResponse.ExceptionInfo exception) {
        return new DebugProtocol.ExceptionInfoResponse(exception.ExceptionId, exception.BreakMode.ToExceptionBreakMode()) {
            Description = exception.Description,
            Code = exception.Code,
            Details = exception.Details.ToExceptionDetails(),
        };
    }
    private static DebugProtocol.ExceptionBreakMode ToExceptionBreakMode(this CorDebugResponse.ExceptionBreakMode breakMode) {
        return breakMode switch {
            CorDebugResponse.ExceptionBreakMode.Always => DebugProtocol.ExceptionBreakMode.Always,
            CorDebugResponse.ExceptionBreakMode.Never => DebugProtocol.ExceptionBreakMode.Never,
            CorDebugResponse.ExceptionBreakMode.Unhandled => DebugProtocol.ExceptionBreakMode.Unhandled,
            CorDebugResponse.ExceptionBreakMode.UserUnhandled => DebugProtocol.ExceptionBreakMode.UserUnhandled,
            _ => DebugProtocol.ExceptionBreakMode.Unknown
        };
    }
    private static DebugProtocol.ExceptionDetails? ToExceptionDetails(this CorDebugResponse.ExceptionInfo.ExceptionDetails? details) {
        if (details == null)
            return null;

        return new DebugProtocol.ExceptionDetails {
            Message = details.Message,
            TypeName = details.TypeName,
            FullTypeName = details.FullTypeName,
            EvaluateName = details.EvaluateName,
            StackTrace = details.StackTrace,
            InnerException = details.InnerException?.Select(it => it.ToExceptionDetails()).ToList(),
        };
    }
}