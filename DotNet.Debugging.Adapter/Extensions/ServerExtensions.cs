using DotNet.Debugging.Adapter.Symbols;
using DotNet.Debugging.Common.Logging;
using DotNet.Debugging.Engine.Enums;
using DotNet.Debugging.Engine.Models;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Breakpoint = DotNet.Debugging.Engine.Models.Breakpoint;
using DebugProtocol = Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace DotNet.Debugging.Adapter.Extensions;

public static class ServerExtensions {
    public static void TrySendEvent(this DebugProtocolClient protocol, DebugEvent ev) {
        try {
            protocol.SendEvent(ev);
        }
        catch (Exception ex) {
            CurrentSessionLogger.Error($"[Handled] {ex.ToString()}");
        }
    }

    public static DebugProtocol.SetVariableResponse ToSetVariableResponse(this DebugProtocol.Variable variable) {
        return new DebugProtocol.SetVariableResponse {
            Value = variable.Value,
            Type = variable.Type,
            VariablesReference = variable.VariablesReference,
            NamedVariables = variable.NamedVariables,
            IndexedVariables = variable.IndexedVariables,
        };
    }
    public static DebugProtocol.GotoTargetsResponse ToJumpToCursorTarget(this DebugProtocol.GotoTargetsArguments args, int id) {
        return new DebugProtocol.GotoTargetsResponse {
            Targets = new List<DebugProtocol.GotoTarget>() {
                new DebugProtocol.GotoTarget {
                    Id = id,
                    Label = "Jump to cursor",
                    Line = args.Line,
                    Column = args.Column,
                }
            }
        };
    }

    public static string ToDisplayMessage(this ExceptionStopInfo exception) {
        return FormatExceptionMessage(exception.Kind, exception.TypeName, exception.ModuleName);
    }
    public static string ToDisplayMessage(this FailedCondition failedCondition) {
        return string.Format(Resources.MsgBreakpointConditionFailed, failedCondition.Breakpoint.Condition, failedCondition.Error);
    }
    // The description names the wrapped exception as well: a client shows the description, not the nested details
    public static DebugProtocol.ExceptionInfoResponse ToExceptionInfoResponse(this ExceptionInfo exception) {
        var description = $"{FormatExceptionMessage(exception.Kind, exception.TypeName, exception.ModuleName)}: '{exception.Message}'";
        var details = CreateExceptionDetails(exception.TypeName, exception.Message, exception.StackTrace);
        var inner = exception.InnerException;
        if (inner != null) {
            description += string.Format(Resources.MsgExceptionInner, inner.TypeName, inner.Message);
            details.InnerException = new List<DebugProtocol.ExceptionDetails> { CreateExceptionDetails(inner.TypeName, inner.Message, inner.StackTrace) };
        }
        return new DebugProtocol.ExceptionInfoResponse(exception.TypeName, exception.Kind.ToBreakMode()) {
            Description = description,
            Details = details,
        };
    }
    // The library pre-populates 'innerException' with an empty list, it is sent only when there is one
    private static DebugProtocol.ExceptionDetails CreateExceptionDetails(string typeName, string message, string? stackTrace) {
        return new DebugProtocol.ExceptionDetails {
            Message = message,
            TypeName = typeName.Substring(typeName.LastIndexOf('.') + 1),
            FullTypeName = typeName,
            EvaluateName = "$exception",
            StackTrace = stackTrace,
            InnerException = null,
        };
    }
    public static DebugProtocol.ExceptionBreakMode ToBreakMode(this ExceptionStopKind kind) {
        return kind switch {
            ExceptionStopKind.Unhandled => DebugProtocol.ExceptionBreakMode.Unhandled,
            ExceptionStopKind.UserUnhandled => DebugProtocol.ExceptionBreakMode.UserUnhandled,
            _ => DebugProtocol.ExceptionBreakMode.Always
        };
    }
    private static string FormatExceptionMessage(ExceptionStopKind kind, string? typeName, string? moduleName) {
        var format = kind switch {
            ExceptionStopKind.Unhandled => Resources.MsgExceptionUnhandled,
            ExceptionStopKind.UserUnhandled => Resources.MsgExceptionUserUnhandled,
            _ => Resources.MsgExceptionThrown
        };
        return string.Format(format, typeName ?? "Exception", moduleName ?? "Unknown Module.");
    }
    public static DebugProtocol.Source ToSource(this SourceLocation location, SourceLinkResolver sourceLinkResolver, SourceFileMapper sourceFileMapper) {
        var filePath = sourceFileMapper.ToLocalPath(location.FilePath);
        // The library pre-populates 'sources' and 'checksums' with empty lists
        var source = new DebugProtocol.Source {
            Name = Path.GetFileName(filePath),
            Path = filePath,
            Sources = null,
            Checksums = null,
        };
        // A Source Link document that does not exist locally is served through the 'source' request, which downloads it
        if (location.SourceLink != null && !File.Exists(filePath))
            source.SourceReference = sourceLinkResolver.GetSourceReference(location.SourceLink);
        return source;
    }
    public static DebugProtocol.Module ToModule(this ModuleInfo module, bool justMyCode) {
        return new DebugProtocol.Module {
            Id = module.Id,
            Name = module.Name,
            Path = module.Path,
            IsOptimized = !module.IsUserCode,
            IsUserCode = module.IsUserCode,
            Version = module.Version?.ToString(),
            SymbolStatus = module.ToSymbolStatus(justMyCode),
            SymbolFilePath = module.HasSymbols ? module.SymbolFilePath : null,
        };
    }
    public static DebugProtocol.Breakpoint ToBreakpoint(this Breakpoint breakpoint, SourceLinkResolver sourceLinkResolver, SourceFileMapper sourceFileMapper) {
        var location = breakpoint.Location;
        return new DebugProtocol.Breakpoint() {
            Id = breakpoint.Id,
            Verified = breakpoint.Verified,
            Message = breakpoint.ToStatusMessage(),
            Line = location != null ? location.Line : breakpoint.IsFunctionBreakpoint ? null : breakpoint.RequestedLine,
            Column = location?.Column,
            EndLine = location?.EndLine,
            EndColumn = location?.EndColumn,
            Source = location?.ToSource(sourceLinkResolver, sourceFileMapper),
        };
    }
    public static DebugProtocol.StackFrame ToStackFrame(this StackFrameInfo frame, SourceLinkResolver sourceLinkResolver, SourceFileMapper sourceFileMapper) {
        return new DebugProtocol.StackFrame() {
            Id = frame.Id,
            Source = frame.Location?.ToSource(sourceLinkResolver, sourceFileMapper),
            Name = frame.ToDisplayName(),
            Line = frame.Location?.Line ?? 0,
            Column = frame.Location?.Column ?? 0,
            EndLine = frame.Location?.EndLine,
            EndColumn = frame.Location?.EndColumn,
            ModuleId = frame.ModuleId,
        };
    }
    public static DebugProtocol.Thread ToThread(this ThreadInfo thread) {
        return new DebugProtocol.Thread(thread.Id, thread.ToDisplayName());
    }
    public static DebugProtocol.Variable ToVariable(this VariableInfo variable) {
        return new DebugProtocol.Variable {
            Name = variable.Name.ToDisplayName(variable.Type),
            Type = variable.Type,
            Value = variable.Value,
            EvaluateName = variable.EvaluateName,
            PresentationHint = variable.ToPresentationHint(),
            VariablesReference = variable.VariablesReference
        };
    }

    public static DebugProtocol.VariablePresentationHint ToPresentationHint(this VariableInfo variable) {
        return new DebugProtocol.VariablePresentationHint {
            Kind = variable.IsError ? null : variable.Kind.ToKindValue(),
            Attributes = variable.ToAttributesValue(),
            Visibility = variable.Visibility?.ToVisibilityValue(),
        };
    }
    public static DebugProtocol.VariablePresentationHint.KindValue ToKindValue(this VariableKind kind) {
        return kind switch {
            VariableKind.Property => DebugProtocol.VariablePresentationHint.KindValue.Property,
            VariableKind.Group => DebugProtocol.VariablePresentationHint.KindValue.Class,
            VariableKind.ResultsView => DebugProtocol.VariablePresentationHint.KindValue.Method,
            _ => DebugProtocol.VariablePresentationHint.KindValue.Data
        };
    }
    public static DebugProtocol.VariablePresentationHint.AttributesValue? ToAttributesValue(this VariableInfo variable) {
        if (variable.IsError)
            return DebugProtocol.VariablePresentationHint.AttributesValue.FailedEvaluation;
        if (variable.Kind == VariableKind.ResultsView)
            return DebugProtocol.VariablePresentationHint.AttributesValue.ReadOnly | DebugProtocol.VariablePresentationHint.AttributesValue.ExpansionHasSideEffects;
        return null;
    }
    public static DebugProtocol.VariablePresentationHint.VisibilityValue ToVisibilityValue(this VariableVisibility visibility) {
        return visibility switch {
            VariableVisibility.Public => DebugProtocol.VariablePresentationHint.VisibilityValue.Public,
            VariableVisibility.Protected => DebugProtocol.VariablePresentationHint.VisibilityValue.Protected,
            VariableVisibility.Internal => DebugProtocol.VariablePresentationHint.VisibilityValue.Internal,
            _ => DebugProtocol.VariablePresentationHint.VisibilityValue.Private
        };
    }
    public static StoppedEvent.ReasonValue ToStoppedReason(this StopReason reason) {
        return reason switch {
            StopReason.Breakpoint => StoppedEvent.ReasonValue.Breakpoint,
            StopReason.Step => StoppedEvent.ReasonValue.Step,
            StopReason.Pause => StoppedEvent.ReasonValue.Pause,
            StopReason.Entry => StoppedEvent.ReasonValue.Entry,
            _ => StoppedEvent.ReasonValue.Unknown
        };
    }
}
